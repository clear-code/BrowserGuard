'use strict';

import { loadConfig } from './config-loader.js';

export const UploadGuard = {
    // onBeforeRequest is blocking, so cache the config for synchronous access.
    enabled: true,
    blockedExtensions: [],
    allowedExtensions: [],
    allowedPatterns: [],
    blockedPatterns: [],

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
        if (Array.isArray(uploadGuard.AllowedExtensions)) {
            this.allowedExtensions = uploadGuard.AllowedExtensions;
        }
        this.allowedPatterns = this.compilePatterns(uploadGuard.AllowedPaths);
        this.blockedPatterns = this.compilePatterns(uploadGuard.BlockedPaths);
    },

    // An unusable pattern is dropped on its own, so one bad entry does not
    // silently turn the whole list into "match everything" or "match nothing".
    compilePatterns(patterns) {
        if (!Array.isArray(patterns)) {
            return [];
        }
        return patterns.map(source => {
            try {
                // Local paths are case insensitive on Windows.
                return new RegExp(source, 'i');
            } catch (error) {
                console.error('Ignoring an invalid path pattern', source, error?.message);
                return null;
            }
        }).filter(Boolean);
    },

    hasExtension(file, extensions) {
        const lower = file.toLowerCase();
        return extensions.some(ext => lower.endsWith(ext.toLowerCase()));
    },

    buildCancelResponse(path, reason, isMainFrame) {
        const message = JSON.stringify(`アップロードがブロックされました:\n${path}\n\n理由: ${reason}`);
        const afterAction = isMainFrame ? 'history.back();' : '';
        const html = `<script>
            alert(${message});
            ${afterAction}
        </script>`;
        return { redirectUrl: `data:text/html;charset=utf-8,${encodeURIComponent(html)}` };
    },

    // Returns null when the file may be uploaded, otherwise the reason to show.
    getBlockReason(file) {
        if (this.blockedPatterns.some(pattern => pattern.test(file))) {
            return 'アップロードが禁止された場所のファイルです';
        }
        if (this.hasExtension(file, this.blockedExtensions)) {
            return '禁止された拡張子です';
        }
        if (this.allowedPatterns.length > 0 &&
            !this.allowedPatterns.some(pattern => pattern.test(file))) {
            return 'アップロードが許可されていない場所のファイルです';
        }
        if (this.allowedExtensions.length > 0 &&
            !this.hasExtension(file, this.allowedExtensions)) {
            return '許可された拡張子ではありません';
        }
        return null;
    },

    onBeforeRequest(details) {
        console.log('UploadGuard onBeforeRequest', details);
        if (!this.enabled) {
            return {};
        }
        if (!details.requestBody?.raw) {
            return {};
        }
        const isMainFrame = (details.type === 'main_frame');
        for (const part of details.requestBody.raw) {
            if (!part.file) {
                continue;
            }
            const reason = this.getBlockReason(part.file);
            if (reason) {
                return this.buildCancelResponse(part.file, reason, isMainFrame);
            }
        }
        return {};
    }
}
