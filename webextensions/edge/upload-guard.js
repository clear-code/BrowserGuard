'use strict';

import { loadConfig } from './config-loader.js';
import { NetLogger } from './net-logger.js';
import { showDialog } from './dialog.js';

export const UploadGuard = {
    // onBeforeRequest is blocking, so cache the config for synchronous access.
    enabled: true,
    blockedExtensions: [],
    allowedExtensions: [],
    allowedPatterns: [],
    blockedPatterns: [],

    async init () {
        const config = await loadConfig();
        this.applyConfig(config?.UploadGuard);
    },

    // Separated from init so that it can be exercised without the browser.
    // Members the config leaves out keep their current value.
    applyConfig(uploadGuard) {
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

    // Cancelling a main frame navigation leaves an error page where the form
    // was, so that one frame is sent back where it came from instead. Anything
    // else is refused outright, so that a script uploading in the background is
    // not handed a page of markup as though its upload had succeeded.
    buildCancelResponse(isMainFrame) {
        if (!isMainFrame) {
            return { cancel: true };
        }
        const html = '<script>history.back();</script>';
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
                // Not awaited: this listener is blocking and has to answer at
                // once. net-logger records nothing unless it is turned on.
                NetLogger.onUploadBlocked({
                    file: part.file,
                    url: details.url,
                    reason,
                    timestamp: details.timeStamp,
                });
                // Not awaited either: the dialog stands until it is dismissed.
                showDialog(`アップロードがブロックされました:\n${part.file}\n\n理由: ${reason}`);
                return this.buildCancelResponse(isMainFrame);
            }
        }
        return {};
    }
}
