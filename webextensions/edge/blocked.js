'use strict';

const blocked = new URLSearchParams(location.search).get('url');
if (blocked) {
  document.getElementById('url').textContent = blocked;
}
