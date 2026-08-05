'use strict';

const blocked = new URLSearchParams(location.search).get('url');
if (blocked) {
  document.getElementById('url').textContent = blocked;
}

// The blocked address may have opened a new tab, leaving nothing to go back to.
// Closing the tab is the way out of that one.
document.getElementById('back').addEventListener('click', async () => {
  if (history.length > 1) {
    history.back();
    return;
  }
  const tab = await chrome.tabs.getCurrent();
  if (tab) {
    chrome.tabs.remove(tab.id);
  }
});
