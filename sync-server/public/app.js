const TOKEN_KEY = 'musikarchiv_web_token';

const state = {
  token: sessionStorage.getItem(TOKEN_KEY),
  pieces: [],
  selectedPiece: null,
  selectedPieceUid: null,
  loadingPieceUid: null
};

const els = {
  loginScreen: document.getElementById('loginScreen'),
  loginForm: document.getElementById('loginForm'),
  loginPassword: document.getElementById('loginPassword'),
  loginError: document.getElementById('loginError'),
  appRoot: document.getElementById('appRoot'),
  logoutButton: document.getElementById('logoutButton'),
  statusBadge: document.getElementById('statusBadge'),
  searchInput: document.getElementById('searchInput'),
  genreFilter: document.getElementById('genreFilter'),
  cabinetFilter: document.getElementById('cabinetFilter'),
  withScoresFilter: document.getElementById('withScoresFilter'),
  activeOnlyFilter: document.getElementById('activeOnlyFilter'),
  pieceCount: document.getElementById('pieceCount'),
  pieceList: document.getElementById('pieceList'),
  backToListButton: document.getElementById('backToListButton'),
  emptyState: document.getElementById('emptyState'),
  detailView: document.getElementById('detailView'),
  detailTitle: document.getElementById('detailTitle'),
  detailMeta: document.getElementById('detailMeta'),
  detailPath: document.getElementById('detailPath'),
  detailTags: document.getElementById('detailTags'),
  detailFields: document.getElementById('detailFields'),
  detailInstruments: document.getElementById('detailInstruments'),
  printPieceArea: document.getElementById('printPieceArea'),
  printPieceButton: document.getElementById('printPieceButton'),
  sheetList: document.getElementById('sheetList')
};

function authHeaders() {
  return state.token ? { Authorization: `Bearer ${state.token}` } : {};
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...authHeaders(),
      ...(options.headers || {})
    }
  });

  if (response.status === 401) {
    logout();
    throw new Error('Sitzung abgelaufen – bitte erneut anmelden.');
  }

  if (!response.ok) {
    throw new Error(`API-Fehler ${response.status}`);
  }

  if (response.status === 204) {
    return null;
  }

  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return response.json();
  }

  throw new Error('Unerwartete Server-Antwort');
}

function showLogin(message = '') {
  els.appRoot.classList.add('hidden');
  els.loginScreen.classList.remove('hidden');
  if (message) {
    els.loginError.textContent = message;
    els.loginError.classList.remove('hidden');
  } else {
    els.loginError.classList.add('hidden');
  }
}

function showApp() {
  els.loginScreen.classList.add('hidden');
  els.appRoot.classList.remove('hidden');
  els.statusBadge.textContent = 'Angemeldet';
  els.statusBadge.className = 'badge ok';
}

function logout() {
  state.token = null;
  sessionStorage.removeItem(TOKEN_KEY);
  setDetailOpen(false);
  showLogin();
}

async function login(password) {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ password })
  });

  if (!response.ok) {
    throw new Error('Ungültiges Passwort');
  }

  const data = await response.json();
  state.token = data.token;
  sessionStorage.setItem(TOKEN_KEY, state.token);
  showApp();
  await bootstrapApp();
}

function debounce(fn, delay) {
  let timer;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), delay);
  };
}

const compactLayoutQuery = window.matchMedia('(orientation: portrait), (max-width: 800px)');

function isCompactLayout() {
  return compactLayoutQuery.matches;
}

function setDetailOpen(open) {
  document.body.classList.toggle('detail-open', Boolean(open));
}

function closePieceDetail() {
  if (parseHashRoute()) {
    window.location.hash = '';
    return;
  }
  setDetailOpen(false);
}

function buildQuery() {
  const params = new URLSearchParams();
  const q = els.searchInput.value.trim();
  if (q) params.set('q', q);
  if (els.genreFilter.value) params.set('genre', els.genreFilter.value);
  if (els.cabinetFilter.value) params.set('cabinet', els.cabinetFilter.value);
  if (els.withScoresFilter.checked) params.set('withScores', '1');
  if (els.activeOnlyFilter.checked) params.set('activeOnly', '1');
  const query = params.toString();
  return query ? `?${query}` : '';
}

async function loadFilters() {
  const { genres, cabinets } = await api('/api/meta/filters');
  els.genreFilter.length = 1;
  els.cabinetFilter.length = 1;
  for (const genre of genres) {
    const option = document.createElement('option');
    option.value = genre;
    option.textContent = genre;
    els.genreFilter.appendChild(option);
  }
  for (const cabinet of cabinets) {
    const option = document.createElement('option');
    option.value = cabinet;
    option.textContent = `Schrank ${cabinet}`;
    els.cabinetFilter.appendChild(option);
  }
}

async function loadPieces() {
  const data = await api(`/api/pieces${buildQuery()}`);
  state.pieces = data.pieces;
  renderPieceList();
}

function renderPieceList() {
  els.pieceCount.textContent = `${state.pieces.length}`;
  els.pieceList.innerHTML = '';

  if (state.pieces.length === 0) {
    const li = document.createElement('li');
    li.innerHTML = '<p class="muted" style="padding:12px">Keine Stücke gefunden.</p>';
    els.pieceList.appendChild(li);
    return;
  }

  for (const piece of state.pieces) {
    const li = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = piece.syncUid === state.selectedPieceUid ? 'active' : '';
    button.innerHTML = `
      <div class="piece-title">${escapeHtml(piece.title)}</div>
      <div class="piece-sub">${escapeHtml(piece.composer || 'Unbekannter Komponist')} · ${piece.sheetCount} Noten</div>
    `;
    button.addEventListener('click', () => selectPiece(piece.syncUid, { focusDetail: isCompactLayout() }));
    button.addEventListener('dblclick', (event) => {
      event.preventDefault();
      selectPiece(piece.syncUid, { focusDetail: true });
    });
    li.appendChild(button);
    els.pieceList.appendChild(li);
  }
}

async function selectPiece(syncUid, { focusDetail = false } = {}) {
  const alreadyLoaded = state.selectedPieceUid === syncUid && state.selectedPiece?.syncUid === syncUid;
  const alreadyLoading = state.loadingPieceUid === syncUid;
  state.selectedPieceUid = syncUid;
  setDetailOpen(true);

  if (alreadyLoaded || alreadyLoading) {
    if (parseHashRoute() !== syncUid) {
      window.location.hash = `#/piece/${syncUid}`;
    }
  } else {
    state.loadingPieceUid = syncUid;
    renderPieceList();
    els.emptyState.classList.add('hidden');
    els.detailView.classList.remove('hidden');
    if (!state.selectedPiece || state.selectedPiece.syncUid !== syncUid) {
      els.detailTitle.textContent = 'Laden …';
    }
    window.location.hash = `#/piece/${syncUid}`;

    try {
      const data = await api(`/api/pieces/${encodeURIComponent(syncUid)}`);
      if (state.selectedPieceUid !== syncUid) return;
      state.selectedPiece = data.piece;
      renderPieceDetail(data.piece, data.sheets);
    } finally {
      if (state.loadingPieceUid === syncUid) {
        state.loadingPieceUid = null;
      }
    }
  }

  if (focusDetail) {
    els.detailView.scrollIntoView({ behavior: 'smooth', block: 'start' });
    els.detailView.focus({ preventScroll: true });
  }
}

function renderPieceDetail(piece, sheets) {
  els.emptyState.classList.add('hidden');
  els.detailView.classList.remove('hidden');

  els.detailTitle.textContent = piece.title;
  els.detailMeta.textContent = [
    piece.composer && `Komponist: ${piece.composer}`,
    piece.genre && `Gattung: ${piece.genre}`,
    piece.cabinet && `Schrank ${piece.cabinet}`
  ].filter(Boolean).join(' · ');
  els.detailPath.textContent = piece.folderPath || '';

  els.detailTags.innerHTML = '';
  const tags = (piece.tags || '').split('#').map((t) => t.trim()).filter(Boolean);
  for (const tag of tags) {
    const span = document.createElement('span');
    span.className = 'tag';
    span.textContent = tag;
    els.detailTags.appendChild(span);
  }

  const fields = [
    ['Komponist', piece.composer],
    ['Arrangeur', piece.arranger],
    ['Verlag', piece.publisher],
    ['ISBN', piece.isbn],
    ['Gattung', piece.genre],
    ['Tags', tags.join(', ') || piece.tags],
    ['Schrank', piece.cabinet],
    ['Fach', piece.compartment],
    ['Einschub', piece.slot],
    ['Ablageort', piece.folderPath],
    ['Im Probelokal', piece.isActive ? 'Ja' : 'Nein'],
    ['Aktualisiert', formatDate(piece.updatedAt)]
  ];

  els.detailFields.innerHTML = fields
    .map(([label, value]) => `<dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value || '–')}</dd>`)
    .join('');

  els.detailInstruments.innerHTML = '';
  for (const name of piece.instrumentNames || []) {
    const li = document.createElement('li');
    li.textContent = name;
    els.detailInstruments.appendChild(li);
  }
  if ((piece.instrumentNames || []).length === 0) {
    const li = document.createElement('li');
    li.textContent = 'Keine Besetzung hinterlegt';
    els.detailInstruments.appendChild(li);
  }

  els.sheetList.innerHTML = '';
  if (sheets.length === 0) {
    const li = document.createElement('li');
    li.innerHTML = '<p class="muted">Keine digitalen Noten vorhanden.</p>';
    els.sheetList.appendChild(li);
    return;
  }

  for (const sheet of sheets) {
    const li = document.createElement('li');
    li.className = 'sheet-present';
    const label = sheet.fileName || 'Note';
    li.innerHTML = `
      <div class="sheet-name">${escapeHtml(`Note ${label} vorhanden`)}</div>
      <div class="sheet-assignment">${escapeHtml(formatAssignment(sheet))}</div>
    `;
    els.sheetList.appendChild(li);
  }
}

function formatAssignment(sheet) {
  if (sheet.instrumentName) return sheet.instrumentName;
  switch (sheet.instrumentGroupId) {
    case 1: return 'Gruppe: Partitur / Direktion';
    case 2: return 'Gruppe: Holz';
    case 3: return 'Gruppe: Schlagwerk';
    case 4: return 'Gruppe: Blechbläser / Gesang';
    default: return 'Allgemein / Gesamt';
  }
}

function printPieceInfo() {
  if (!state.selectedPiece) return;
  document.body.classList.add('printing-piece');
  window.print();
  window.addEventListener('afterprint', () => document.body.classList.remove('printing-piece'), { once: true });
}

function parseHashRoute() {
  const match = window.location.hash.match(/^#\/piece\/([^/?#]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}

async function bootstrapApp() {
  await loadFilters();
  await loadPieces();
  const routePiece = parseHashRoute();
  if (routePiece) {
    await selectPiece(routePiece);
  }
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function formatDate(value) {
  if (!value) return '–';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('de-DE');
}

els.loginForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  els.loginError.classList.add('hidden');
  try {
    await login(els.loginPassword.value);
  } catch (error) {
    els.loginError.textContent = error.message;
    els.loginError.classList.remove('hidden');
  }
});

els.logoutButton.addEventListener('click', logout);
els.backToListButton.addEventListener('click', closePieceDetail);
els.printPieceButton.addEventListener('click', printPieceInfo);

const reloadPieces = debounce(() => {
  loadPieces().catch((error) => {
    els.statusBadge.textContent = error.message;
    els.statusBadge.className = 'badge error';
  });
}, 250);

els.searchInput.addEventListener('input', reloadPieces);
els.genreFilter.addEventListener('change', () => loadPieces());
els.cabinetFilter.addEventListener('change', () => loadPieces());
els.withScoresFilter.addEventListener('change', () => loadPieces());
els.activeOnlyFilter.addEventListener('change', () => loadPieces());

window.addEventListener('hashchange', async () => {
  const routePiece = parseHashRoute();
  if (!routePiece) {
    setDetailOpen(false);
    return;
  }
  if (routePiece !== state.selectedPieceUid) {
    await selectPiece(routePiece);
  } else {
    setDetailOpen(true);
  }
});

window.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && document.body.classList.contains('detail-open') && isCompactLayout()) {
    closePieceDetail();
  }
});

async function init() {
  try {
    await fetch('/api/health');
    if (state.token) {
      const status = await api('/api/auth/status');
      if (status.authenticated) {
        showApp();
        await bootstrapApp();
        return;
      }
      logout();
    }
    showLogin();
  } catch (error) {
    showLogin('Server nicht erreichbar');
  }
}

init();
