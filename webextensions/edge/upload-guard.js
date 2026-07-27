'use strict';

import { loadConfig } from './config-loader.js';

export const UploadGuard = {
    // onBeforeRequest は blocking なので同期的に参照できるようキャッシュしておく。
    enabled: true,
    blockedExtensions: [".exe", ".bat", ".cmd", ".js", ".vbs"],

    async init () {
        const config = await loadConfig();
        const uploadGuard = config?.UploadGuard;
        if (!uploadGuard) return;
        if (typeof uploadGuard.Enabled === 'boolean') {
            this.enabled = uploadGuard.Enabled;
        }
        if (Array.isArray(uploadGuard.BlockedExtensions)) {
            this.blockedExtensions = uploadGuard.BlockedExtensions;
        }
    },

    buildCancelResponse(path, isMainFrame) {
        const message = JSON.stringify(`アップロードがブロックされました:\n${path}`);
        const afterAction = isMainFrame ? 'history.back();' : '';
        const html = `<script>
            alert(${message});
            ${afterAction}
        </script>`;
        return { redirectUrl: `data:text/html;charset=utf-8,${encodeURIComponent(html)}` };
    },

    isBlocked(file) {
        const lower = file.toLowerCase();
        return this.blockedExtensions.some(ext => lower.endsWith(ext));
    },

    onBeforeRequest(details) {
        console.log('onBeforeRequest', details);
        if (!this.enabled) {
            return {};
        }
        if (!details.requestBody?.raw) {
            return {};
        }
        const isMainFrame = (details.type === 'main_frame');
        for (const part of details.requestBody.raw) {
            if (part.file && this.isBlocked(part.file)) {
                return this.buildCancelResponse(part.file, isMainFrame);
            }
        }
        return {};
    }
}
