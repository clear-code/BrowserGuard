'use strict';

const REASONS = {
  continuous: '連続して使用できる時間の上限に達しました。',
  schedule: '使用が許可された時間帯を過ぎています。',
};

const params = new URLSearchParams(location.search);
const deadline = Number(params.get('deadline')) || 0;

document.getElementById('reason').textContent =
  REASONS[params.get('reason')] || '使用時間の制限を超過しました。';

const countdown = document.getElementById('countdown');
const note = document.getElementById('note');

if (!deadline) {
  note.textContent = '作業中の内容は、早めに保存してください。';
} else {
  note.textContent = '作業中の内容は、いますぐ保存してください。';
  const tick = () => {
    const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
    countdown.textContent = `あと ${left} 秒でブラウザーを終了します。`;
    if (left > 0) return;
    clearInterval(timer);
    // The service worker owns the shutdown; this page only reports the time.
    chrome.runtime.sendMessage({ type: 'usage-time-limit:expired' });
  };
  const timer = setInterval(tick, 1000);
  tick();
}
