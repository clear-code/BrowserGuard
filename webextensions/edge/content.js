'use strict';

chrome.runtime.onMessage.addListener((msg) => {
  if (msg.type === "blocked") {
    alert(`アップロードがブロックされました:\n${msg.path}`);
  }
});