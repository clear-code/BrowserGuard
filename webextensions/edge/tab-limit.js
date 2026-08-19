'use strict';

const params = new URLSearchParams(location.search);
const count = Number(params.get('count')) || 0;
const max = Number(params.get('max')) || 0;

document.getElementById('reason').textContent =
  '同時に開くことのできるタブの数の上限を超えています。';

// The service worker owns the counting; this page only reports what it was
// told, so a warning left open cannot show a number that was never reached.
document.getElementById('count').textContent =
  `開いているタブは ${count} 個です。上限は ${max} 個です。`;
