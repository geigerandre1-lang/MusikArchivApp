const TOKEN_KEY = 'musikarchiv_web_token';

const state = {
  token: sessionStorage.getItem(TOKEN_KEY),
  allPieces: [],
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
  wipeButton: document.getElementById('wipeButton'),
  wipeDialog: document.getElementById('wipeDialog'),
  wipeForm: document.getElementById('wipeForm'),
  wipeConfirmInput: document.getElementById('wipeConfirmInput'),
  wipeCancelButton: document.getElementById('wipeCancelButton'),
  wipeError: document.getElementById('wipeError'),
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

function showStatus(message, isError = false) {
  els.statusBadge.textContent = message;
  els.statusBadge.className = isError ? 'badge error' : 'badge ok';
}

function authHeaders() {
  return state.token ? { Authorization: `Bearer ${state.token}` } : {};
}

async function api(path, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const headers = {
    ...authHeaders(),
    ...(options.headers || {})
  };
  if (method !== 'GET' && method !== 'HEAD' && !headers['Content-Type'] && !headers['content-type']) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(path, {
    ...options,
    headers
  });

  if (response.status === 401) {
    logout();
    throw new Error('Sitzung abgelaufen – bitte erneut anmelden.');
  }

  if (!response.ok) {
    let detail = `API-Fehler ${response.status}`;
    try {
      const payload = await response.json();
      if (payload?.error) {
        detail = payload.error;
      }
    } catch {
      /* keep status text */
    }
    throw new Error(detail);
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

function instrumentNamesOf(piece) {
  const value = piece?.instrumentNames;
  if (Array.isArray(value)) {
    return value.map((item) => String(item)).filter(Boolean);
  }
  if (value == null || value === '') {
    return [];
  }
  if (typeof value === 'string') {
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed.map((item) => String(item)).filter(Boolean) : [];
    } catch {
      return value ? [value] : [];
    }
  }
  if (typeof value === 'object') {
    return Object.values(value).map((item) => String(item)).filter(Boolean);
  }
  return [];
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
  showStatus('Angemeldet');
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
  document.body.classList.toggle('detail-open', Boolean(open) && isCompactLayout());
}

function closePieceDetail() {
  if (parseHashRoute()) {
    window.location.hash = '';
    return;
  }
  setDetailOpen(false);
}

function syncCompactDetailClass() {
  if (state.selectedPieceUid && isCompactLayout()) {
    setDetailOpen(true);
  } else {
    document.body.classList.remove('detail-open');
  }
}

async function loadFilters() {
  const { genres, cabinets } = await api('/api/meta/filters');
  const selectedGenre = els.genreFilter.value;
  const selectedCabinet = els.cabinetFilter.value;
  els.genreFilter.length = 1;
  els.cabinetFilter.length = 1;
  for (const genre of genres || []) {
    const option = document.createElement('option');
    option.value = genre;
    option.textContent = genre;
    els.genreFilter.appendChild(option);
  }
  for (const cabinet of cabinets || []) {
    const option = document.createElement('option');
    option.value = cabinet;
    option.textContent = `Schrank ${cabinet}`;
    els.cabinetFilter.appendChild(option);
  }
  if ([...els.genreFilter.options].some((option) => option.value === selectedGenre)) {
    els.genreFilter.value = selectedGenre;
  }
  if ([...els.cabinetFilter.options].some((option) => option.value === selectedCabinet)) {
    els.cabinetFilter.value = selectedCabinet;
  }
}

function applyFilters() {
  const q = els.searchInput.value.trim().toLowerCase();
  const genre = els.genreFilter.value;
  const cabinet = els.cabinetFilter.value;
  const withScores = els.withScoresFilter.checked;
  const activeOnly = els.activeOnlyFilter.checked;

  state.pieces = state.allPieces.filter((piece) => {
    if (activeOnly && !piece.isActive) {
      return false;
    }
    if (genre && piece.genre !== genre) {
      return false;
    }
    if (cabinet && piece.cabinet !== cabinet) {
      return false;
    }
    if (withScores && !(Number(piece.sheetCount) > 0)) {
      return false;
    }
    if (q) {
      const haystack = [
        piece.title,
        piece.composer,
        piece.arranger,
        piece.tags,
        piece.folderPath,
        instrumentNamesOf(piece).join(' ')
      ]
        .join(' ')
        .toLowerCase();
      if (!haystack.includes(q)) {
        return false;
      }
    }
    return true;
  });
  renderPieceList();
}

async function loadPieces() {
  try {
    const data = await api('/api/pieces');
    state.allPieces = Array.isArray(data?.pieces) ? data.pieces : [];
    applyFilters();
  } catch (error) {
    showStatus(error.message, true);
  }
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
      <div class="piece-sub">${escapeHtml(piece.composer || 'Unbekannter Komponist')} · ${Number(piece.sheetCount) || 0} Noten</div>
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
      renderPieceDetail(data.piece, data.sheets || []);
    } catch (error) {
      if (state.selectedPieceUid !== syncUid) return;
      showStatus(error.message, true);
      els.detailTitle.textContent = 'Details konnten nicht geladen werden';
      els.detailMeta.textContent = error.message;
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

  const instrumentNames = instrumentNamesOf(piece);
  els.detailInstruments.innerHTML = '';
  for (const name of instrumentNames) {
    const li = document.createElement('li');
    li.textContent = name;
    els.detailInstruments.appendChild(li);
  }
  if (instrumentNames.length === 0) {
    const li = document.createElement('li');
    li.textContent = 'Keine Besetzung hinterlegt';
    els.detailInstruments.appendChild(li);
  }

  els.sheetList.innerHTML = '';
  if (!Array.isArray(sheets) || sheets.length === 0) {
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
  try {
    await loadFilters();
  } catch (error) {
    showStatus(`Filter: ${error.message}`, true);
  }
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

function closeWipeDialog() {
  if (els.wipeDialog?.open) {
    els.wipeDialog.close();
  }
  if (els.wipeConfirmInput) {
    els.wipeConfirmInput.value = '';
  }
  if (els.wipeError) {
    els.wipeError.classList.add('hidden');
    els.wipeError.textContent = '';
  }
}

function openWipeDialog() {
  if (!els.wipeDialog) {
    return;
  }
  els.wipeError.classList.add('hidden');
  els.wipeConfirmInput.value = '';
  if (typeof els.wipeDialog.showModal === 'function') {
    els.wipeDialog.showModal();
  } else {
    els.wipeDialog.setAttribute('open', '');
  }
  els.wipeConfirmInput.focus();
}

async function submitWipe(event) {
  event.preventDefault();
  const typed = els.wipeConfirmInput.value.trim();
  if (typed !== 'LÖSCHEN') {
    els.wipeError.textContent = 'Bitte genau LÖSCHEN eingeben.';
    els.wipeError.classList.remove('hidden');
    return;
  }

  try {
    const result = await api('/api/web/wipe', {
      method: 'POST',
      body: JSON.stringify({ confirm: 'LÖSCHEN' })
    });
    closeWipeDialog();
    state.selectedPiece = null;
    state.selectedPieceUid = null;
    window.location.hash = '';
    setDetailOpen(false);
    els.emptyState.classList.remove('hidden');
    els.detailView.classList.add('hidden');
    await bootstrapApp();
    showStatus(
      `Web-Datenbank geleert (${result?.pieces ?? 0} Stücke, ${result?.sheets ?? 0} Noten). Passwort bleibt erhalten.`
    );
  } catch (error) {
    els.wipeError.textContent = error.message;
    els.wipeError.classList.remove('hidden');
    showStatus(error.message, true);
  }
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

if (els.wipeButton) {
  els.wipeButton.addEventListener('click', openWipeDialog);
}
if (els.wipeCancelButton) {
  els.wipeCancelButton.addEventListener('click', closeWipeDialog);
}
if (els.wipeForm) {
  els.wipeForm.addEventListener('submit', submitWipe);
}
if (els.wipeDialog) {
  els.wipeDialog.addEventListener('cancel', (event) => {
    event.preventDefault();
    closeWipeDialog();
  });
}

const reloadFilters = debounce(() => {
  try {
    applyFilters();
  } catch (error) {
    showStatus(error.message, true);
  }
}, 250);

function handleFilterChange() {
  try {
    applyFilters();
  } catch (error) {
    showStatus(error.message, true);
  }
}

els.searchInput.addEventListener('input', reloadFilters);
els.genreFilter.addEventListener('change', handleFilterChange);
els.cabinetFilter.addEventListener('change', handleFilterChange);
els.withScoresFilter.addEventListener('change', handleFilterChange);
els.activeOnlyFilter.addEventListener('change', handleFilterChange);

if (typeof compactLayoutQuery.addEventListener === 'function') {
  compactLayoutQuery.addEventListener('change', syncCompactDetailClass);
} else if (typeof compactLayoutQuery.addListener === 'function') {
  compactLayoutQuery.addListener(syncCompactDetailClass);
}

window.addEventListener('hashchange', async () => {
  const routePiece = parseHashRoute();
  if (!routePiece) {
    setDetailOpen(false);
    return;
  }
  if (routePiece !== state.selectedPieceUid) {
    try {
      await selectPiece(routePiece);
    } catch (error) {
      showStatus(error.message, true);
    }
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
