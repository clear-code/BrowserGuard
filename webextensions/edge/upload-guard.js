'use strict';

export const UploadGuard = {
    blockedExtensions: [".exe", ".bat", ".cmd", ".js", ".vbs"],

    init () {
        // Initialization logic if needed
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
