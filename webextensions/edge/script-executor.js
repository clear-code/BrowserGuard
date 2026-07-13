'use strict';

export const StartupLauncher = {
  async restoreIfNeeded() {
    if (!chrome.userScripts) return;
    try {
      const { scripts = [] } = await chrome.storage.local.get('scripts');
      const active = scripts.filter(s =>
        s.enabled && s.code?.trim() && s.matches?.trim()
      );
      if (active.length === 0) return;

      const registered = await chrome.userScripts.getScripts();
      const registeredIds = new Set(registered.map(s => s.id));
      const toRegister = active
        .filter(s => !registeredIds.has(s.id))
        .map(s => ({
          id: s.id,
          matches: s.matches.split('\n').map(m => m.trim()).filter(Boolean),
          js: [{ code: s.code }],
          world: s.world === 'MAIN' ? 'MAIN' : 'USER_SCRIPT',
          runAt: 'document_idle',
        }))
        .filter(s => s.matches.length > 0);

      if (toRegister.length) await chrome.userScripts.register(toRegister);
    } catch { /* noop */ }
  }
}