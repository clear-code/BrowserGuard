'use strict';

window.addEventListener('beforeprint', () => {
  chrome.runtime.sendMessage({
    type: 'print',
    url: location.href,
    title: document.title,
    timestamp: Date.now(),
  });
});