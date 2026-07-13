'use strict';

export const BlockSettingPage = {
  urlPrefixes: [{ urlPrefix: 'edge://settings/' }],
  init() {
  },
  onBeforeNavigate(details) {
    if (details.frameId !== 0) return; 
    chrome.tabs.goBack(details.tabId).catch(() =>
      chrome.tabs.update(details.tabId, { url: 'about:blank' })
    );
    chrome.notifications.create('settings-blocked', {
      type: 'basic',
      iconUrl: 'misc/128x128.png',
      title: '設定画面へのアクセスはブロックされています',
      message: '拡張機能のポリシーにより edge://settings/ は表示できません。',
    });
  },
}

