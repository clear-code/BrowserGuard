'use strict';

const params = new URLSearchParams(location.search);
const max = Number(params.get('max')) || 0;

document.getElementById('closed').textContent =
  `上限は ${max} 個です。`;
