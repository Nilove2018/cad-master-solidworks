// CAD Master panel: chat, tasks, terminal, history, settings and account.
(() => {
  'use strict';

  // ------------------------------------------------------------------ helpers
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const icon = (n, cls) => `<svg class="i ${cls || ''}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${(window.ICONS || {})[n] || ''}</svg>`;
  const hydrate = (root) => $$('[data-icon]', root).forEach((n) => { n.insertAdjacentHTML('afterbegin', icon(n.dataset.icon)); n.removeAttribute('data-icon'); });
  const h = (html) => { const t = document.createElement('template'); t.innerHTML = html.trim(); hydrate(t.content); return t.content.firstElementChild; };
  const md = (text) => {
    const html = DOMPurify.sanitize(marked.parse(String(text || ''), { gfm: true, breaks: false }));
    const div = document.createElement('div');
    div.className = 'md';
    div.innerHTML = html;
    $$('pre', div).forEach((pre) => {
      const b = h(`<button class="ibtn copy" title="Copy">${icon('copy')}</button>`);
      b.onclick = () => copy(pre.innerText);
      pre.appendChild(b);
    });
    $$('a', div).forEach((a) => a.addEventListener('click', (e) => { e.preventDefault(); bridge.send('open_url', { url: a.href }); }));
    fileChips(div);
    return div;
  };

  // Paths to design files in Claude's answers become chips: click to open, drag out to Explorer or an email.
  const FILE_EXT = 'step|stp|igs|iges|stl|3mf|obj|x_t|x_b|sldprt|sldasm|slddrw|pdf|png|jpe?g|dxf|dwg|csv|xlsx|html?|md|txt';
  const PATH_RE = new RegExp('^[A-Za-z]:\\\\[^<>"|?*\\n]+\\.(' + FILE_EXT + ')$', 'i');
  const PATH_IN_TEXT = new RegExp('[A-Za-z]:\\\\[^\\s<>"|?*`]+\\.(' + FILE_EXT + ')\\b', 'gi');
  const fileIcon = (p) => /\.(png|jpe?g)$/i.test(p) ? 'photo' : /\.(pdf|dxf|dwg|slddrw)$/i.test(p) ? 'ruler-2' : /\.(csv|xlsx|md|txt|html?)$/i.test(p) ? 'file-text' : 'file-3d';
  const chipHtml = (p) => `<span class="fchip" draggable="true" data-path="${esc(p)}" title="${esc(p)}\nClick to open · drag to copy it out">${icon(fileIcon(p))}<span>${esc(p.split('\\').pop())}</span></span>`;
  function fileChips(root) {
    $$('code', root).forEach((c) => {
      const t = c.textContent.trim();
      if (!c.closest('pre') && PATH_RE.test(t)) c.replaceWith(h(chipHtml(t)));
    });
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, { acceptNode: (n) => n.parentElement.closest('pre, code, .fchip') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT });
    const hits = [];
    while (walker.nextNode()) { PATH_IN_TEXT.lastIndex = 0; if (PATH_IN_TEXT.test(walker.currentNode.nodeValue)) hits.push(walker.currentNode); }
    hits.forEach((n) => {
      PATH_IN_TEXT.lastIndex = 0;
      const span = document.createElement('span');
      span.innerHTML = esc(n.nodeValue).replace(PATH_IN_TEXT, (p) => chipHtml(p.replace(/&amp;/g, '&')));
      hydrate(span);
      n.replaceWith(...span.childNodes);
    });
  }
  function fileMenu(chip) {
    const p = chip.dataset.path;
    const inSw = /\.(sldprt|sldasm|slddrw|step|stp|igs|iges|x_t|x_b)$/i.test(p);
    openMenu(chip, [
      { label: inSw ? 'Open in SOLIDWORKS' : 'Open', icon: inSw ? 'cube' : 'external-link', run: () => send('open_file', { path: p }) },
      { label: 'Show in folder', icon: 'folder', run: () => send('open_path', { path: p }) },
      { label: 'Attach to my next message', icon: 'paperclip', run: () => send('attach_paths', { paths: [p] }) },
      { label: 'Copy path', icon: 'copy', run: () => copy(p) },
    ]);
  }
  const copy = (text) => { navigator.clipboard.writeText(text).then(() => toast('ok', 'Copied')); };
  const trunc = (s, n) => (s && s.length > n ? s.slice(0, n) + '\n… (' + (s.length - n) + ' more characters)' : s || '');
  const plural = (n, w) => n + ' ' + w + (n === 1 ? '' : 's');
  const send = (type, data) => bridge.send(type, data);

  // ------------------------------------------------------------------ state
  const S = {
    settings: {}, auth: null, version: '', swConnected: false,
    mode: 'plan', model: 'default', effort: '',
    session: 'none', sessionId: null, busy: false, workspace: '',
    ctx: { title: '', path: '', selected: 0 },
    init: null, slash: [], attachments: [], tools: {}, todos: [], plan: null, planTitle: '',
    alwaysAllow: new Set(), driving: new Set(), stream: null, transcript: [], usage: null,
    term: null, fit: null, termReady: false, tab: 'chat',
    activity: null, lastShot: null, turnStart: 0, turnShots: [],
  };

  const MODES = [
    { id: 'plan', label: 'Plan', icon: 'list-check', hint: 'Plans first. Nothing changes until you approve' },
    { id: 'default', label: 'Ask', icon: 'help', hint: 'Asks before every change' },
    { id: 'acceptEdits', label: 'Accept edits', icon: 'pencil-check', hint: 'Asks only before driving SOLIDWORKS' },
    { id: 'auto', label: 'Auto', icon: 'sparkles', hint: 'Asks only for risky actions' },
    { id: 'bypassPermissions', label: 'Bypass', icon: 'rocket', hint: 'Claude decides and builds without asking' },
  ];
  const MODELS = [['default', 'Default'], ['fable', 'Fable'], ['opus', 'Opus'], ['sonnet', 'Sonnet'], ['haiku', 'Haiku']];
  const EFFORTS = [['', 'Effort'], ['low', 'Low'], ['medium', 'Medium'], ['high', 'High'], ['xhigh', 'Extra high'], ['max', 'Max'], ['ultracode', 'Ultracode']];
  const EFFORT_HINTS = { ultracode: 'Deepest reasoning, for hard designs (Opus and Fable)' };

  // Workflows on the ribbon. Each uses the CAD Master plugin command when it is loaded, else the text prompt.
  const CAD = {
    review: { cmd: 'review', title: 'Review model', text: 'Review the active SOLIDWORKS document without changing it. Load the solidworks-api skill, use sw_status, sw_feature_tree, sw_check and a screenshot, then report problems and improvements: design intent, fully defined sketches, rebuild errors, wall thickness and manufacturability.' },
    drawing: { cmd: 'drawing', title: 'Make drawing', text: 'Plan a drawing of the active part following the solidworks-api skill (sections 8.2 and 8.3: sheet layout, views, ISO 129 spacing, tolerances and GD&T checklist). Show me the layout plan first, then build it in a new drawing document.' },
    checks: { cmd: 'check', title: 'Run checks', text: 'Run the solidworks-api skill section 12 verification checklist on the active document (rebuild, What\'s Wrong, sketches, bodies, mates, interference for assemblies) and report the results with numbers. Do not change the model.' },
    export: { cmd: 'export', title: 'Export STEP / STL', text: 'Export the active document as STEP (AP214) and STL (binary, mm) next to the file, following the solidworks-api skill section 8 (Extension.SaveAs3 with swSaveAsOptions_Copy, restore every preference you change). Report the file paths and sizes.' },
    testrun: { cmd: 'testrun', title: 'Test run', text: 'Do a SolidWorks Test Run (skill rule 1.14) for the API behaviour we need next, in a new untitled document, then close only that document.' },
  };

  // ------------------------------------------------------------------ tabs and chrome
  function showTab(tab) {
    S.tab = tab;
    $$('.tabs .tab').forEach((b) => b.classList.toggle('on', b.dataset.tab === tab));
    $('#chatView').classList.toggle('hidden', tab !== 'chat');
    $('#tasksView').classList.toggle('hidden', tab !== 'tasks');
    $('#detailsView').classList.toggle('hidden', tab !== 'details');
    $('#termView').classList.toggle('hidden', tab !== 'terminal');
    $('#historyView').classList.toggle('hidden', tab !== 'history');
    if (tab === 'terminal') { ensureTerm(); setTimeout(() => { fitTerm(); S.term && S.term.focus(); }, 30); }
    if (tab === 'history') { $('#historyView').innerHTML = '<div class="note">Loading…</div>'; send('list_sessions'); }
    if (tab === 'tasks') renderTasks();
    if (tab === 'chat') setTimeout(() => $('#input').focus(), 30);
  }

  function applySettings() {
    const st = S.settings || {};
    document.documentElement.dataset.theme = st.Theme || 'dark';
    document.documentElement.style.setProperty('--fs', (st.FontSize || 13) + 'px');
  }

  function renderHeader() {
    const line = $('#ctxLine');
    let dot = 'dot', text;
    if (S.busy) { dot += ' busy'; text = 'Working' + (S.ctx.title ? ' · ' + S.ctx.title : ''); }
    else if (!S.swConnected) { dot += ' err'; text = 'SOLIDWORKS not connected'; }
    else if (S.ctx.title) { dot += ' ok'; text = S.ctx.title + (S.ctx.selected ? ' · ' + plural(S.ctx.selected, 'item') + ' selected' : ''); }
    else { dot += ' ok'; text = 'SOLIDWORKS · no document open'; }
    line.innerHTML = `<span class="${dot}"></span>${esc(text)}`;
    const a = S.auth && S.auth.status;
    const email = a && (a.email || a.emailAddress);
    $('#avatarBtn').textContent = email ? email.slice(0, 2).toUpperCase() : '?';
    $('#avatarBtn').title = email ? 'Signed in as ' + email : 'Sign in';
  }

  function renderChips() {
    const m = MODES.find((x) => x.id === S.mode) || MODES[1];
    const mc = $('#modeChip');
    mc.className = 'chip mode-' + m.id;
    mc.innerHTML = icon(m.icon) + esc(m.label) + icon('chevron-down');
    const model = MODELS.find((x) => x[0] === S.model);
    $('#modelChip').innerHTML = esc(model ? model[1] : S.model) + icon('chevron-down');
    const eff = EFFORTS.find((x) => x[0] === (S.effort || ''));
    $('#effortChip').innerHTML = esc(eff ? eff[1] : S.effort) + icon('chevron-down');
    const sb = $('#sendBtn');
    sb.className = 'send' + (S.busy ? ' stop' : '');
    sb.innerHTML = icon(S.busy ? 'player-stop' : 'arrow-up');
    sb.title = S.busy ? 'Stop (Esc)' : 'Send (Enter)';
    $('#input').placeholder = S.busy ? 'Queue a message for Claude' : S.mode === 'plan' ? 'Describe what to design, or drop a picture or STEP file' : 'Ask CAD Master, drop files, or type / for commands';
  }

  function renderStatus() {
    const a = S.auth;
    let left = a ? (a.loggedIn ? 'Signed in' : a.installed === false ? 'Claude Code not installed' : 'Signed out') : '';
    if (a && a.version) left += ' · ' + a.version.replace(' (Claude Code)', '');
    if (S.init && S.init.model) left += ' · ' + S.init.model.replace('claude-', '');
    $('#statusLeft').textContent = left;
    let right = '';
    if (S.usage) right = S.usage;
    $('#statusRight').textContent = right;
  }

  function toast(level, text) {
    const t = h(`<div class="toast ${esc(level)}">${esc(text)}</div>`);
    $('#toasts').appendChild(t);
    setTimeout(() => t.remove(), level === 'error' ? 7000 : 3000);
  }

  // ------------------------------------------------------------------ menus
  function openMenu(anchor, items, opts) {
    closeMenus();
    const m = h('<div class="menu"></div>');
    items.forEach((it) => {
      if (it === '-') { m.appendChild(h('<div class="sep"></div>')); return; }
      if (it.header) { m.appendChild(h(`<div class="hd">${esc(it.header)}</div>`)); return; }
      const row = h(`<div class="mi ${it.current ? 'cur' : ''}">${it.icon ? icon(it.icon) : ''}<span>${esc(it.label)}</span>${it.hint ? `<small>${esc(it.hint)}</small>` : ''}</div>`);
      row.onclick = () => { closeMenus(); it.run(); };
      m.appendChild(row);
    });
    document.body.appendChild(m);
    const r = anchor.getBoundingClientRect();
    const w = m.offsetWidth, hgt = m.offsetHeight;
    let left = (opts && opts.alignRight) ? r.right - w : r.left;
    left = Math.max(6, Math.min(left, window.innerWidth - w - 6));
    let top = r.bottom + 4;
    if (top + hgt > window.innerHeight - 6) top = Math.max(6, r.top - hgt - 4);
    m.style.left = left + 'px';
    m.style.top = top + 'px';
    setTimeout(() => document.addEventListener('mousedown', outside), 0);
    function outside(e) { if (!m.contains(e.target)) { closeMenus(); document.removeEventListener('mousedown', outside); } }
  }
  function closeMenus() { $$('body > .menu').forEach((m) => m.remove()); }

  function modeMenu(anchor) {
    openMenu(anchor, MODES.map((m) => ({
      label: m.label, icon: m.icon, hint: m.hint, current: m.id === S.mode, run: () => setMode(m.id),
    })));
  }
  function setMode(mode) {
    if (mode === 'bypassPermissions' && !S.settings.AllowBypass) { confirmBypass(() => setMode(mode)); return; }
    S.mode = mode; renderChips(); send('set_mode', { mode });
  }

  // Bypass lets Claude take the design decisions and run every step without asking: confirm it once.
  function confirmBypass(then) {
    const b = overlay('Turn on Bypass?', `
      <p>In <b>Bypass</b> Claude works like an engineer you've handed the job to: it makes the design decisions itself and builds every step without stopping to ask. At the end it lists what it assumed.</p>
      <p class="sub">It still never closes, overwrites or saves over your own documents. Use it for new designs, and keep backups of important files.</p>
      <div class="btns" style="display:flex;gap:6px"><button class="btn pri" id="bpOn">${icon('rocket')}Turn on Bypass</button><button class="btn" id="bpNo">Cancel</button></div>`);
    $('#bpNo', b).onclick = closeOverlay;
    $('#bpOn', b).onclick = () => {
      S.settings = Object.assign({}, S.settings, { AllowBypass: true });
      send('save_settings', { settings: S.settings });
      closeOverlay();
      then();
    };
  }

  function modelMenu(anchor) {
    const items = MODELS.map(([id, label]) => ({ label, current: id === S.model, run: () => { S.model = id; renderChips(); send('set_model', { model: id }); } }));
    items.push('-', { label: 'Custom model ID…', icon: 'pencil', run: () => {
      const id = prompt('Model name or ID (for example claude-opus-5-5):', S.model === 'default' ? '' : S.model);
      if (id) { S.model = id.trim(); renderChips(); send('set_model', { model: S.model }); }
    } });
    openMenu(anchor, items);
  }
  function effortMenu(anchor) {
    openMenu(anchor, EFFORTS.map(([id, label]) => ({ label: id ? label : 'Default', icon: id === 'ultracode' ? 'sparkles' : null, hint: EFFORT_HINTS[id], current: id === (S.effort || ''), run: () => { S.effort = id; renderChips(); send('set_effort', { effort: id }); } })));
  }

  function mainMenu(anchor) {
    openMenu(anchor, [
      { label: 'New chat', icon: 'message-plus', run: newChat },
      { label: 'New design brief…', icon: 'clipboard-list', run: showBrief },
      { label: 'History', icon: 'history', run: () => showTab('history') },
      '-',
      { label: 'Export chat (.md)', icon: 'download', run: exportChat },
      { label: 'Open this chat in terminal', icon: 'terminal-2', run: () => { showTab('terminal'); startTerm(true); } },
      { label: 'Open workspace folder', icon: 'folder', hint: S.workspace ? S.workspace.split('\\').pop() : '', run: () => send('open_path', { path: S.workspace }) },
      { label: 'Change workspace folder…', icon: 'folder', run: () => send('pick_workspace') },
      '-',
      { label: 'Settings', icon: 'settings', run: showSettings },
      { label: 'Account', icon: 'user-circle', run: showAccount },
      { label: 'Open logs', icon: 'file-text', run: () => send('open_logs') },
      { label: 'About CAD Master', icon: 'help', run: showAbout },
    ], { alignRight: true });
  }

  // ------------------------------------------------------------------ chat rendering
  const msgs = () => $('#messages');
  function scrollDown(force) {
    const m = msgs();
    if (force || m.scrollHeight - m.scrollTop - m.clientHeight < 140) m.scrollTop = m.scrollHeight;
  }
  function add(node) { $('.empty', msgs()) && $('.empty', msgs()).remove(); msgs().appendChild(node); scrollDown(); return node; }

  // The Details tab: everything behind the scenes (tools, scripts, results, thinking) for those who want it.
  const dlog = () => $('#detailLog');
  function addDetail(node) {
    const l = dlog();
    const stick = l.parentElement.scrollHeight - l.parentElement.scrollTop - l.parentElement.clientHeight < 140;
    $('.empty', l) && $('.empty', l).remove();
    l.appendChild(node);
    if (stick) l.parentElement.scrollTop = l.parentElement.scrollHeight;
    return node;
  }
  function renderDetailsEmpty() {
    dlog().innerHTML = `<div class="empty"><h2>The detailed process</h2><p>Every command, script, check result and thought Claude goes through shows up here. You don't need it to design; it's for when you want to see exactly what happened.</p></div>`;
  }

  // ------------------------------------------------------------------ activity line: what Claude is doing, in design terms
  const SW_WORDS = { sw_status: 'Checking the open document', sw_selection: 'Reading your selection', sw_feature_tree: 'Reading the feature tree', sw_screenshot: 'Looking at the model', sw_check: 'Running model checks', sw_mass_properties: 'Calculating mass properties', sw_custom_properties: 'Reading part properties', sw_run_macro: 'Running a macro in SOLIDWORKS' };
  const GUESS = [
    [/interfer|clearance|collision/i, 'Checking clearances'],
    [/drawing|slddrw/i, 'Making the drawing'],
    [/export|\.step\b|\.stp\b|\.stl\b|\.iges?\b|\.3mf\b/i, 'Exporting files'],
    [/\bmates?\b|mating/i, 'Mating parts'],
    [/assembl|sldasm/i, 'Assembling'],
    [/thread|helix/i, 'Modelling the thread'],
    [/fillet/i, 'Adding fillets'], [/chamfer/i, 'Adding chamfers'], [/\bshell/i, 'Shelling the body'],
    [/loft/i, 'Lofting'], [/sweep/i, 'Sweeping'], [/revol/i, 'Revolving'], [/extru|\bboss/i, 'Extruding'],
    [/pattern/i, 'Patterning features'], [/mirror/i, 'Mirroring'], [/draft/i, 'Adding draft'], [/\bcut\b/i, 'Cutting'],
    [/sketch|profile/i, 'Sketching'],
    [/mass|weight|volume/i, 'Calculating mass'],
    [/material/i, 'Applying material'], [/render|appearance|colou?r/i, 'Styling the appearance'],
    [/param|fit table|equation/i, 'Setting up parameters'],
    [/check|verif|what'?s ?wrong|rebuild|\btest/i, 'Checking the model'],
    [/\bsav(e|ing)\b/i, 'Saving'],
  ];
  const CODEY = /python|\.py\b|script|\bpip\b|powershell|\bbash\b|grep|\bls\b|\bcat\b|\bcd\b|json|module|import|install|print|debug|traceback|regex|swlib|\bapi\b/i;
  const words = (s) => s.replace(/[_-]+/g, ' ').replace(/\s+/g, ' ').trim();
  function guessActivity(text) {
    const m = /\b(build|check|make|export|probe)_([A-Za-z0-9_]+)\.py/i.exec(text || '');
    if (m) return { build: 'Building the ', check: 'Checking the ', make: 'Making the ', export: 'Exporting the ', probe: 'Measuring the ' }[m[1].toLowerCase()] + words(m[2]);
    const g = GUESS.find(([re]) => re.test(text || ''));
    return g ? g[1] : null;
  }
  function friendly(name, input) {
    input = input || {};
    if (name === 'Bash' || name === 'PowerShell') {
      const d = (input.description || '').trim().replace(/\.$/, '');
      if (d && !CODEY.test(d)) return d[0].toUpperCase() + d.slice(1);
      return guessActivity(d + ' ' + (input.command || '')) || 'Working on the model';
    }
    if (name === 'Write' || name === 'Edit' || name === 'MultiEdit') { const g = guessActivity(input.file_path || ''); return g ? g.replace(/^\w+ing/, 'Preparing') : 'Preparing the next step'; }
    if (name === 'Read' || name === 'Glob' || name === 'Grep') return 'Reviewing project notes';
    if (name === 'Skill') return 'Loading the SOLIDWORKS design rules';
    if (name === 'TodoWrite' || name === 'TaskCreate' || name === 'TaskUpdate') return 'Updating the task list';
    if (name === 'ExitPlanMode') return 'Plan ready for your approval';
    if (name === 'AskUserQuestion') return 'Waiting for your answer';
    if (name === 'Task' || name === 'Agent') return 'Design review: ' + (input.description || 'second opinion');
    if (name === 'WebSearch' || name === 'WebFetch') return 'Looking up references';
    if (name.startsWith('mcp__solidworks__')) return SW_WORDS[name.slice(17)] || 'Reading SOLIDWORKS';
    return 'Working';
  }

  function setActivity(text) {
    S.activity = text;
    renderActivity();
  }
  function currentStep() {
    const i = S.todos.findIndex((t) => t.status === 'in_progress');
    return i < 0 ? '' : `step ${i + 1} of ${S.todos.length}`;
  }
  function renderActivity() {
    const el = $('#activity');
    el.classList.toggle('hidden', !S.busy);
    if (!S.busy) return;
    const step = currentStep();
    const line = (S.activity || 'Getting started') + (step ? ' · ' + step : '');
    const at = $('.at', el);
    if (at.textContent !== line) { at.textContent = line; at.title = line; }
    $('.hands', el).classList.toggle('hidden', !S.driving.size);
    const th = $('.thumb', el);
    th.classList.toggle('hidden', !S.lastShot);
    if (S.lastShot && th.src !== S.lastShot) th.src = S.lastShot;
    const secs = Math.max(0, Math.round((Date.now() - (S.turnStart || Date.now())) / 1000));
    $('.el', el).textContent = Math.floor(secs / 60) + ':' + String(secs % 60).padStart(2, '0');
  }
  function startTurn() {
    S.turnStart = Date.now(); S.activity = 'Getting started'; S.lastShot = null; S.turnShots = [];
  }
  const fmtDuration = (ms) => { const s = Math.round(ms / 1000); return s < 60 ? s + ' s' : Math.floor(s / 60) + ' min ' + (s % 60 ? (s % 60) + ' s' : ''); };

  function renderEmpty() {
    const m = msgs();
    m.innerHTML = '';
    const e = h(`<div class="empty">
      <img class="logo" src="img/logo.png" alt="">
      <h2>What are we designing today?</h2>
      <p>Describe a part, or drop a picture, sketch or STEP file here and say "make this". Claude plans it like a design engineer, then builds and checks it in SOLIDWORKS.</p>
      <div class="starters"></div></div>`);
    const starters = [
      ['clipboard-list', 'Plan a new part', showBrief],
      ['zoom-check', 'Review the active model', () => runCad('review')],
      ['pointer', 'Explain my selection', () => { send('attach_selection'); $('#input').value = 'Explain the selected geometry and how it was made.'; $('#input').focus(); }],
      ['ruler-2', 'Make a drawing', () => runCad('drawing')],
      ['list-check', 'Run the checks', () => runCad('checks')],
      ['file-export', 'Export STEP and STL', () => runCad('export')],
    ];
    starters.forEach(([ic, label, fn]) => {
      const b = h(`<button class="starter">${icon(ic)}<span>${esc(label)}</span></button>`);
      b.onclick = fn;
      $('.starters', e).appendChild(b);
    });
    m.appendChild(e);
  }

  const CTX_RE = /<cad-context>[\s\S]*?<\/cad-context>\s*/g;

  function addUser(text, atts) {
    const b = h('<div class="ub"></div>');
    b.textContent = text.replace(CTX_RE, '');
    if (atts && atts.length) {
      const row = h('<div class="atts"></div>');
      atts.forEach((a) => row.appendChild(a.kind === 'image'
        ? h(`<img class="shot" draggable="true" data-name="${esc(a.name)}" src="data:${esc(a.mediaType)};base64,${a.data}" alt="">`)
        : a.path ? h(chipHtml(a.path)) : h(`<span class="cx">${icon('file-text')}<span>${esc(a.name)}</span></span>`)));
      b.appendChild(row);
    }
    add(b);
    S.transcript.push({ role: 'You', text: text.replace(CTX_RE, '') });
  }

  function addNote(text, err) { return add(h(`<div class="note ${err ? 'err' : ''}">${esc(text)}</div>`)); }

  function textBlock(text) {
    const d = md(text);
    S.transcript.push({ role: 'Claude', text });
    return add(d);
  }

  function thinkingBlock(text) {
    if (S.settings.ShowThinking === false || !text) return null;
    const d = h(`<details class="think"><summary>${icon('brain')}Thinking</summary></details>`);
    d.appendChild(md(text));
    return addDetail(d);
  }

  // Tool cards, keyed by tool_use id.
  function toolMeta(name, input) {
    input = input || {};
    const short = (p) => (p || '').split(/[\\/]/).slice(-2).join('\\');
    if (name === 'Bash' || name === 'PowerShell') return { ic: 'terminal-2', nm: name, sm: input.description || input.command };
    if (name === 'Read') return { ic: 'file-text', nm: 'Read', sm: short(input.file_path) };
    if (name === 'Write') return { ic: 'file-plus', nm: 'Write', sm: short(input.file_path) };
    if (name === 'Edit' || name === 'MultiEdit' || name === 'NotebookEdit') return { ic: 'pencil', nm: 'Edit', sm: short(input.file_path || input.notebook_path) };
    if (name === 'Glob' || name === 'Grep') return { ic: 'search', nm: name, sm: input.pattern };
    if (name === 'WebFetch' || name === 'WebSearch') return { ic: 'world', nm: name, sm: input.url || input.query };
    if (name === 'Task' || name === 'Agent') return { ic: 'sparkles', nm: 'Agent', sm: input.description };
    if (name === 'Skill') return { ic: 'book-2', nm: 'Skill', sm: input.skill || input.command, gold: true };
    if (name === 'TodoWrite' || name === 'TaskCreate' || name === 'TaskUpdate') return { ic: 'list-check', nm: 'Tasks', sm: name === 'TaskCreate' ? input.subject : 'task list updated', gold: true };
    if (name === 'ExitPlanMode') return { ic: 'clipboard-list', nm: 'Plan', sm: 'ready for approval', gold: true };
    if (name === 'AskUserQuestion') return { ic: 'question-mark', nm: 'Question', sm: (input.questions || []).map((q) => q.header || q.question).join(', ') };
    if (name.startsWith('mcp__solidworks__')) return { ic: 'cube', nm: name.slice(17), sm: Object.keys(input).length ? JSON.stringify(input) : DRIVE_NAMES[name.slice(17)] || '' };
    if (name.startsWith('mcp__')) return { ic: 'tool', nm: name.split('__').slice(1).join(' · '), sm: '' };
    return { ic: 'tool', nm: name, sm: '' };
  }
  const DRIVE_NAMES = { sw_status: 'active document', sw_selection: 'your selection', sw_feature_tree: 'feature tree', sw_screenshot: 'current view', sw_check: 'checks', sw_mass_properties: 'mass', sw_custom_properties: 'properties' };

  function toolCard(id, name, input) {
    let t = S.tools[id];
    const meta = toolMeta(name, input);
    if (!t) {
      const el = h(`<details class="act"><summary><span class="ic ${meta.gold ? 'gold' : ''}">${icon(meta.ic)}</span><span class="nm"></span><span class="sm"></span><span class="st">${icon('loader-2', 'run')}</span></summary><div class="body"></div></details>`);
      t = S.tools[id] = { id, name, el, done: false };
      addDetail(el);
    }
    if (!t.done && (input && Object.keys(input).length || !t.input)) setActivity(friendly(name, input));
    t.input = input;
    $('.nm', t.el).textContent = meta.nm;
    $('.sm', t.el).textContent = meta.sm || '';
    $('.sm', t.el).title = meta.sm || '';
    const body = $('.body', t.el);
    body.innerHTML = '';
    if (input && Object.keys(input).length) {
      const shown = name === 'Bash' || name === 'PowerShell' ? input.command : name === 'Write' ? input.content : JSON.stringify(input, null, 2);
      body.appendChild(h('<div class="lbl">Input</div>'));
      const pre = document.createElement('pre');
      pre.textContent = trunc(shown, 4000);
      body.appendChild(pre);
    }
    if (!t.done && (name === 'Bash' || name === 'PowerShell') && input && /python|\.py\b|swlib|sldworks|\.swb|\.swp/i.test(input.command || '')) {
      S.driving.add(id);
      renderActivity();
    }
    if (name === 'TodoWrite' && input && input.todos) { S.todos = input.todos.map((x) => ({ content: x.content, status: x.status, active: x.activeForm })); renderTasks(); }
    if (name === 'TaskCreate' && input && input.subject) { S.todos.push({ id: String(S.todos.length + 1), content: input.subject, status: 'pending', note: input.description }); renderTasks(); }
    if (name === 'TaskUpdate' && input) {
      const task = S.todos.find((x) => x.id === String(input.taskId)) || S.todos[Number(input.taskId) - 1];
      if (task) { if (input.status) task.status = input.status === 'deleted' ? 'cancelled' : input.status; if (input.subject) task.content = input.subject; renderTasks(); }
    }
    if (name === 'ExitPlanMode' && input && input.plan) { S.plan = input.plan; renderTasks(); }
    return t;
  }

  function toolResult(id, content, isError) {
    const t = S.tools[id];
    if (!t) return;
    t.done = true;
    $('.st', t.el).innerHTML = icon(isError ? 'circle-x' : 'check', isError ? 'bad' : 'ok');
    const body = $('.body', t.el);
    const blocks = typeof content === 'string' ? [{ type: 'text', text: content }] : (content || []);
    const texts = blocks.filter((b) => b.type === 'text').map((b) => b.text).join('\n');
    if (texts) {
      body.appendChild(h(`<div class="lbl">${isError ? 'Error' : 'Result'}</div>`));
      const pre = document.createElement('pre');
      pre.textContent = trunc(texts, 6000);
      body.appendChild(pre);
    }
    blocks.filter((b) => b.type === 'image' && b.source && b.source.data).forEach((b) => {
      const src = `data:${esc(b.source.media_type || 'image/png')};base64,${b.source.data}`;
      t.el.after(h(`<img class="shot" draggable="true" data-name="model view" alt="" src="${src}">`));
      S.lastShot = src;
      S.turnShots.push(src);
    });
    S.driving.delete(id);
    renderActivity();
  }

  // Streaming (--include-partial-messages): previews replaced by the final assistant message.
  function onStreamEvent(ev) {
    if (!ev) return;
    if (ev.type === 'message_start') { S.stream = { pending: [] }; return; }
    if (!S.stream) S.stream = { pending: [] };
    if (ev.type === 'content_block_start') {
      const b = ev.content_block || {};
      if (b.type === 'text' || b.type === 'thinking') {
        const el = b.type === 'text' ? add(md('')) : (S.settings.ShowThinking === false ? null : addDetail(h(`<details class="think" open><summary>${icon('brain')}Thinking…</summary><div class="md"></div></details>`)));
        if (b.type === 'thinking') setActivity('Thinking it through');
        S.stream.pending.push({ index: ev.index, type: b.type, el, text: '' });
      } else if (b.type === 'tool_use') {
        toolCard(b.id, b.name, {});
      }
    } else if (ev.type === 'content_block_delta') {
      const p = S.stream.pending.find((x) => x.index === ev.index);
      if (!p || !p.el) return;
      const d = ev.delta || {};
      p.text += d.text || d.thinking || '';
      if (!p.raf) p.raf = requestAnimationFrame(() => {
        p.raf = 0;
        const target = p.type === 'text' ? p.el : $('.md', p.el);
        if (!target) return;
        const fresh = md(p.text);
        target.innerHTML = fresh.innerHTML;
        scrollDown();
      });
    }
  }

  function onAssistant(msg) {
    const content = (msg.message && msg.message.content) || [];
    content.forEach((b) => {
      if (b.type === 'text') {
        const p = S.stream && takePending('text');
        if (p && p.el) { if (p.raf) cancelAnimationFrame(p.raf); p.el.replaceWith(md(b.text)); S.transcript.push({ role: 'Claude', text: b.text }); scrollDown(); }
        else if (b.text && b.text.trim()) textBlock(b.text);
      } else if (b.type === 'thinking') {
        const p = S.stream && takePending('thinking');
        if (p && p.el) {
          if (p.raf) cancelAnimationFrame(p.raf);
          p.el.open = false;
          $('summary', p.el).innerHTML = icon('brain') + 'Thinking';
          if (b.thinking) $('.md', p.el).innerHTML = md(b.thinking).innerHTML;
          else p.el.remove();
        } else thinkingBlock(b.thinking);
      } else if (b.type === 'tool_use') {
        toolCard(b.id, b.name, b.input);
      }
    });
  }
  function takePending(type) {
    const i = S.stream.pending.findIndex((x) => x.type === type);
    return i < 0 ? null : S.stream.pending.splice(i, 1)[0];
  }

  function onUserMessage(msg) {
    const content = msg.message && msg.message.content;
    if (!Array.isArray(content)) return;
    content.forEach((b) => { if (b.type === 'tool_result') toolResult(b.tool_use_id, b.content, b.is_error); });
  }

  function onResult(msg) {
    S.busy = false;
    S.stream = null;
    S.driving.clear();
    renderActivity();
    $$('.act .run').forEach((n) => n.closest('.st') && (n.closest('.st').innerHTML = icon('circle', '')));
    const secs = msg.duration_ms ? (msg.duration_ms / 1000).toFixed(1) + ' s' : '';
    const u = msg.usage || {};
    const tokens = (u.input_tokens || 0) + (u.cache_read_input_tokens || 0) + (u.cache_creation_input_tokens || 0) + (u.output_tokens || 0);
    const bits = [secs, msg.num_turns ? plural(msg.num_turns, 'turn') : '', tokens ? Math.round(tokens / 1000) + 'k tokens' : ''].filter(Boolean);
    if (msg.is_error || (msg.subtype && msg.subtype !== 'success')) addNote((msg.result || (msg.errors || []).join('\n') || msg.subtype || 'Claude stopped with an error.'), true);
    if (bits.length) addDetail(h(`<div class="result">${esc(bits.join(' · '))}</div>`));
    // The chat shows the latest model view of this turn instead of every screenshot along the way.
    if (S.turnShots.length) {
      const last = S.turnShots[S.turnShots.length - 1];
      add(h(`<figure class="mview"><img class="shot" draggable="true" data-name="model view" alt="" src="${last}"><figcaption>${icon('cube')}Model view${S.turnShots.length > 1 ? ` · ${S.turnShots.length - 1} more in Details` : ''}</figcaption></figure>`));
    }
    if (msg.duration_ms > 8000) add(h(`<div class="result">${icon('check')}Done in ${esc(fmtDuration(msg.duration_ms))}</div>`));
    S.turnShots = [];
    renderHeader(); renderChips();
  }

  function onClaude(msg) {
    if (!msg || msg.parent_tool_use_id) return; // sub-agent internals stay inside their Agent card
    switch (msg.type) {
      case 'system':
        if (msg.subtype === 'init') {
          S.init = msg;
          S.slash = msg.slash_commands || [];
          if (msg.permissionMode) S.mode = msg.permissionMode;
          renderChips(); renderStatus();
        } else if (msg.subtype === 'status' && msg.permissionMode) { S.mode = msg.permissionMode; renderChips(); }
        else if (msg.subtype === 'compact_boundary') addNote('Conversation compacted');
        break;
      case 'stream_event': onStreamEvent(msg.event); break;
      case 'assistant': onAssistant(msg); break;
      case 'user': onUserMessage(msg); break;
      case 'result': onResult(msg); break;
      case 'rate_limit_event': {
        const r = msg.rate_limit_info || {};
        const w = r.unifiedWindows && r.unifiedWindows.five_hour;
        if (w && typeof w.utilization === 'number') { S.usage = '5-hour usage ' + Math.round(w.utilization * 100) + '%'; renderStatus(); }
        if (r.status && r.status !== 'allowed' && r.status !== 'allowed_warning') addNote('Usage limit reached. Claude will continue when it resets.', true);
        break;
      }
    }
  }

  // ------------------------------------------------------------------ permission, plan and question cards
  function respond(requestId, response) { send('permission_response', { requestId, response }); }

  function onPermission(m) {
    const r = m.request || {};
    const tool = r.tool_name;
    if (tool === 'ExitPlanMode') return planCard(m.requestId, r);
    if (tool === 'AskUserQuestion') return questionCard(m.requestId, r);
    if (S.alwaysAllow.has(tool)) return respond(m.requestId, { behavior: 'allow', updatedInput: r.input });
    const isRun = tool === 'Bash' || tool === 'PowerShell';
    const title = isRun ? 'OK to run this step in SOLIDWORKS?'
      : tool === 'Write' || tool === 'Edit' || tool === 'MultiEdit' ? 'OK to save a working file?'
      : tool === 'mcp__solidworks__sw_run_macro' ? 'OK to run a macro in SOLIDWORKS?'
      : tool === 'WebFetch' || tool === 'WebSearch' ? 'OK to look this up online?'
      : 'Allow ' + (r.display_name || toolMeta(tool, r.input).nm) + '?';
    const card = h(`<div class="card perm"><div class="ch">${icon('hand-stop')}<span></span></div>
      <div class="what"></div>
      <details class="tech"><summary>Technical details</summary><pre></pre></details>
      <input type="text" class="reason" placeholder="If you say no: what should Claude do instead?" style="width:100%">
      <div class="btns"><button class="btn pri" data-a="allow">Yes</button><button class="btn" data-a="always">Yes, don't ask again in this chat</button><button class="btn danger" data-a="deny">No</button></div></div>`);
    $('.ch span', card).textContent = title;
    $('.what', card).textContent = friendly(tool, r.input);
    $('pre', card).textContent = trunc(isRun ? (r.input || {}).command : JSON.stringify(r.input, null, 2), 3000);
    $('.reason', card).classList.add('field');
    $$('.btns .btn', card).forEach((b) => b.onclick = () => {
      const a = b.dataset.a;
      let verdict;
      if (a === 'deny') {
        const reason = $('.reason', card).value.trim();
        respond(m.requestId, { behavior: 'deny', message: reason || 'The user denied this action.' });
        verdict = 'No' + (reason ? ': ' + reason : '');
      } else {
        const resp = { behavior: 'allow', updatedInput: r.input };
        if (a === 'always') { S.alwaysAllow.add(tool); if (r.permission_suggestions && r.permission_suggestions.length) resp.updatedPermissions = r.permission_suggestions; }
        respond(m.requestId, resp);
        verdict = a === 'always' ? 'Allowed for the rest of this chat' : 'Allowed';
      }
      finishCard(card, verdict);
    });
    add(card);
    scrollDown(true);
    notifyTab();
  }

  function finishCard(card, verdict) {
    card.classList.add('done');
    $$('button', card).forEach((x) => x.disabled = true);
    $$('input, textarea', card).forEach((x) => x.remove());
    const btns = $('.btns', card);
    if (btns) btns.remove();
    card.appendChild(h(`<div class="verdict">${esc(verdict)}</div>`));
  }

  function planCard(requestId, r) {
    const plan = (r.input && r.input.plan) || '';
    S.plan = plan;
    const card = h(`<div class="card plan"><div class="ch">${icon('clipboard-list')}Plan ready for your approval</div><div class="plan-body"></div>
      <textarea placeholder="Changes you want (used with Keep planning)" rows="2"></textarea>
      <div class="btns">
        <button class="btn pri" data-a="bypass">${icon('rocket')}Approve, Claude builds it all</button>
        <button class="btn" data-a="build">Approve, I confirm each run</button>
        <button class="btn" data-a="keep">Keep planning</button></div></div>`);
    $('.plan-body', card).appendChild(md(plan));
    $$('.btns .btn', card).forEach((b) => b.onclick = () => {
      const a = b.dataset.a;
      if (a === 'keep') {
        const fb = $('textarea', card).value.trim();
        respond(requestId, { behavior: 'deny', message: 'The user wants to keep planning before building.' + (fb ? ' Feedback: ' + fb : ' Ask what to change.') });
        finishCard(card, 'Kept planning' + (fb ? ': ' + fb : ''));
        return;
      }
      const approve = () => {
        const nextMode = a === 'bypass' ? 'bypassPermissions' : 'acceptEdits';
        respond(requestId, { behavior: 'allow', updatedInput: r.input, updatedPermissions: [{ type: 'setMode', mode: nextMode, destination: 'session' }] });
        S.mode = nextMode;
        renderChips();
        finishCard(card, a === 'bypass' ? 'Approved. Claude builds and checks every step without asking' : 'Approved. Claude asks before each run in SOLIDWORKS');
        renderTasks();
      };
      if (a === 'bypass' && !S.settings.AllowBypass) confirmBypass(approve); else approve();
    });
    add(card);
    renderTasks();
    scrollDown(true);
    notifyTab();
  }

  function questionCard(requestId, r) {
    const qs = (r.input && r.input.questions) || [];
    const card = h(`<div class="card ask"><div class="ch">${icon('question-mark')}Claude has ${qs.length > 1 ? 'some questions' : 'a question'}</div><div class="qs"></div>
      <div class="btns"><button class="btn pri" data-a="send">Send answers</button><button class="btn" data-a="skip">Skip</button></div></div>`);
    const answers = {};
    qs.forEach((q, qi) => {
      const box = h(`<div style="margin-bottom:8px"><div style="font-weight:600;margin-bottom:3px"></div></div>`);
      box.firstChild.textContent = q.question;
      const chosen = new Set();
      (q.options || []).forEach((o) => {
        const opt = h(`<div class="opt"><div><b></b><small></small></div></div>`);
        $('b', opt).textContent = o.label;
        $('small', opt).textContent = o.description || '';
        opt.onclick = () => {
          if (!q.multiSelect) { chosen.clear(); $$('.opt', box).forEach((x) => x.classList.remove('sel')); }
          if (chosen.has(o.label)) { chosen.delete(o.label); opt.classList.remove('sel'); } else { chosen.add(o.label); opt.classList.add('sel'); }
          answers[q.question] = Array.from(chosen).join(', ');
        };
        box.appendChild(opt);
      });
      const other = h('<input type="text" placeholder="Other answer" style="width:100%;margin-top:4px">');
      other.classList.add('field');
      other.oninput = () => { if (other.value.trim()) answers[q.question] = other.value.trim(); };
      box.appendChild(other);
      $('.qs', card).appendChild(box);
    });
    $$('.btns .btn', card).forEach((b) => b.onclick = () => {
      if (b.dataset.a === 'skip') { respond(requestId, { behavior: 'deny', message: 'The user skipped the question. Use your best judgement and say what you assumed.' }); finishCard(card, 'Skipped'); return; }
      respond(requestId, { behavior: 'allow', updatedInput: Object.assign({}, r.input, { answers }) });
      finishCard(card, Object.entries(answers).map(([k, v]) => v).join(' · ') || 'Answered');
    });
    add(card);
    scrollDown(true);
    notifyTab();
  }

  function notifyTab() { if (S.tab !== 'chat') showTab('chat'); }

  // ------------------------------------------------------------------ composer
  function renderCtxChips() {
    const c = $('#ctxChips');
    c.innerHTML = '';
    if (S.ctx.title && S.settings.AutoAttachContext !== false) {
      c.appendChild(h(`<span class="cx" title="Sent with your message: active document and selection">${icon('box')}<span>${esc(S.ctx.title)}</span>${S.ctx.selected ? ' · ' + plural(S.ctx.selected, 'item') : ''}</span>`));
    }
    S.attachments.forEach((a, i) => {
      const chip = h(`<span class="cx">${a.kind === 'image' ? `<img src="data:${esc(a.mediaType)};base64,${a.data}" alt="">` : icon('file-text')}<span>${esc(a.name)}</span><button title="Remove">${icon('x')}</button></span>`);
      $('button', chip).onclick = () => { S.attachments.splice(i, 1); renderCtxChips(); };
      if (a.kind === 'image') $('img', chip).onclick = () => lightbox(`data:${a.mediaType};base64,${a.data}`);
      c.appendChild(chip);
    });
  }

  function contextPrefix(text) {
    if (text.startsWith('/')) return '';
    const bits = [];
    if (S.settings.AutoAttachContext !== false && S.ctx.title) bits.push(`SOLIDWORKS active document: ${S.ctx.title}${S.ctx.path ? ' (' + S.ctx.path + ')' : ' (not saved yet)'}; ${plural(S.ctx.selected, 'item')} selected.`);
    if (S.mode === 'bypassPermissions') bits.push('Panel mode: Bypass. The user wants you to take the design decisions yourself: do not stop to ask, choose sound engineering values, and list your assumptions at the end.');
    return bits.length ? `<cad-context>${bits.join(' ')}</cad-context>\n` : '';
  }

  function submit(textOverride) {
    const input = $('#input');
    const text = (textOverride != null ? textOverride : input.value).trim();
    if (!text && !S.attachments.length) return;
    if (S.auth && !S.auth.loggedIn) { showWelcome(); return; }
    const atts = S.attachments.splice(0);
    let full = contextPrefix(text) + text;
    atts.filter((a) => a.kind === 'text').forEach((a) => { full += `\n\n[${a.name}]\n${a.text}`; });
    atts.filter((a) => a.kind === 'image' && a.path).forEach((a) => { full += `\n\n[Attached image file: ${a.path}]`; });
    const content = [{ type: 'text', text: full || 'See the attached image.' }];
    atts.filter((a) => a.kind === 'image').forEach((a) => content.push({ type: 'image', source: { type: 'base64', media_type: a.mediaType, data: a.data } }));
    addUser(text || '(attachment)', atts);
    send('send', { content });
    if (textOverride == null) { input.value = ''; autosize(); }
    S.lastInput = text;
    if (!S.busy) startTurn();
    S.busy = true;
    renderCtxChips(); renderChips(); renderHeader(); renderActivity();
    scrollDown(true);
  }

  function runCad(id) {
    const c = CAD[id];
    if (!c) return;
    showTab('chat');
    const slash = S.slash.find((s) => s === 'cad:' + c.cmd || s === 'cad-' + c.cmd);
    submit(slash ? '/' + slash : c.text);
  }

  function autosize() {
    const i = $('#input');
    i.style.height = 'auto';
    i.style.height = Math.min(220, Math.max(52, i.scrollHeight + 2)) + 'px';
  }

  // Slash command palette
  function updatePalette() {
    const v = $('#input').value;
    const pal = $('#palette');
    if (!v.startsWith('/') || v.includes(' ') || v.includes('\n')) { pal.classList.add('hidden'); return; }
    const q = v.slice(1).toLowerCase();
    const cmds = S.slash.length ? S.slash : ['compact', 'clear', 'context', 'cost', 'review'];
    const list = cmds.filter((c) => c.toLowerCase().includes(q)).slice(0, 12);
    if (!list.length) { pal.classList.add('hidden'); return; }
    pal.innerHTML = '';
    list.forEach((c, i) => {
      const row = h(`<div class="mi ${i === 0 ? 'act-item' : ''}"><span>/${esc(c)}</span></div>`);
      row.onclick = () => { $('#input').value = '/' + c + ' '; pal.classList.add('hidden'); $('#input').focus(); };
      pal.appendChild(row);
    });
    pal.classList.remove('hidden');
  }

  // ------------------------------------------------------------------ tasks view
  function renderTasks() {
    const v = $('#tasksView');
    const todos = S.todos;
    const open = todos.filter((t) => t.status !== 'completed' && t.status !== 'cancelled').length;
    const badge = $('#taskBadge');
    badge.textContent = open;
    badge.classList.toggle('hidden', !open);
    renderActivity();
    if (S.tab !== 'tasks') return;
    v.innerHTML = '';
    if (!todos.length && !S.plan) {
      v.appendChild(h(`<div class="empty"><img class="logo" src="img/logo.png" alt=""><h2>No tasks yet</h2><p>Start a design in Plan mode. When you approve Claude's plan, its steps show up here and tick off as they're built and checked.</p><button class="btn pri" id="tb">${icon('clipboard-list')}New design brief</button></div>`));
      $('#tb', v).onclick = showBrief;
      return;
    }
    const done = todos.filter((t) => t.status === 'completed').length;
    const title = (S.plan && (S.plan.match(/^#+\s*(.+)$/m) || [])[1]) || 'Current tasks';
    v.appendChild(h(`<div class="tt"></div>`)).textContent = title;
    if (todos.length) {
      v.appendChild(h(`<div class="bar"><div style="width:${Math.round(100 * done / todos.length)}%"></div></div>`));
      v.appendChild(h(`<div class="sub">${done} of ${todos.length} done</div>`));
      todos.forEach((t) => {
        const st = t.status === 'completed' ? 'done' : t.status === 'in_progress' ? 'now' : 'todo';
        const ic = st === 'done' ? 'circle-check' : st === 'now' ? 'loader-2' : t.status === 'cancelled' ? 'circle-x' : 'circle';
        const row = h(`<div class="step ${st}">${icon(ic)}<div><span class="c"></span>${st === 'now' && t.active && t.active !== t.content ? '<div class="note2"></div>' : ''}</div></div>`);
        $('.c', row).textContent = t.content;
        if ($('.note2', row)) $('.note2', row).textContent = t.active;
        row.onclick = () => openMenu(row, [
          { label: 'Explain this step', icon: 'help', run: () => { showTab('chat'); submit('Explain the task "' + t.content + '": what you did or will do, and how it is checked.'); } },
          { label: 'Redo this step', icon: 'refresh', run: () => { showTab('chat'); submit('Redo the task "' + t.content + '" and verify it again.'); } },
          { label: 'Skip this step', icon: 'x', run: () => { showTab('chat'); submit('Skip the task "' + t.content + '" and continue with the next one.'); } },
        ]);
        v.appendChild(row);
      });
    }
    if (S.driving.size) v.appendChild(h(`<div class="banner" style="margin:0">${icon('hand-stop')}Claude is working in SOLIDWORKS. Don't click in the model for a moment.</div>`));
    if (S.plan) {
      const d = h(`<details class="planbox" ${todos.length ? '' : 'open'}><summary>${icon('clipboard-list')}Plan</summary></details>`);
      d.appendChild(md(S.plan));
      v.appendChild(d);
    }
  }

  // ------------------------------------------------------------------ terminal
  function ensureTerm() {
    if (S.term || typeof Terminal === 'undefined') return;
    const t = new Terminal({
      fontFamily: '"Cascadia Mono", Consolas, monospace', fontSize: 13, cursorBlink: true, scrollback: 5000,
      theme: { background: '#0A0F1C', foreground: '#F3E7CF', cursor: '#D8A548', selectionBackground: '#3A4670', black: '#1D2640', red: '#E5484D', green: '#3FB37F', yellow: '#D8A548', blue: '#5B8DEF', magenta: '#C678DD', cyan: '#4FC3D9', white: '#F3E7CF' },
    });
    const fit = new FitAddon.FitAddon();
    t.loadAddon(fit);
    t.open($('#term'));
    t.onData((d) => send('term_input', { data: d }));
    t.attachCustomKeyEventHandler((e) => {
      if (e.type === 'keydown' && e.ctrlKey && e.key === 'c' && t.hasSelection()) { copy(t.getSelection()); return false; }
      return true;
    });
    S.term = t; S.fit = fit;
    new ResizeObserver(() => fitTerm()).observe($('#term'));
    t.writeln('\x1b[38;2;216;165;72mCAD Master terminal\x1b[0m: the full Claude Code command line.');
    t.writeln('Press \x1b[1mStart Claude Code\x1b[0m for a new session, or \x1b[1mOpen this chat\x1b[0m to continue the chat here.');
  }
  function fitTerm() {
    if (!S.fit || $('#termView').classList.contains('hidden')) return;
    try { S.fit.fit(); send('term_resize', { cols: S.term.cols, rows: S.term.rows }); } catch (e) { }
  }
  function startTerm(resume) {
    ensureTerm(); fitTerm();
    if (resume && !S.sessionId) { toast('warn', 'No chat to open yet. Starting a new session.'); resume = false; }
    S.term.reset();
    send('term_start', resume ? { resume: S.sessionId } : {});
  }

  // ------------------------------------------------------------------ history
  function renderHistory(list, workspace) {
    const v = $('#historyView');
    v.innerHTML = '';
    v.appendChild(h(`<div class="sub" style="margin-bottom:4px">Chats in <b></b></div>`));
    $('b', v).textContent = workspace || '';
    if (!list || !list.length) { v.appendChild(h('<div class="note">No saved chats in this folder yet.</div>')); return; }
    list.forEach((s) => {
      const row = h(`<div class="hist"><div class="h1"></div><div class="h2"><span></span><span class="sp"></span><button class="ibtn" title="Open in terminal">${icon('terminal-2')}</button></div></div>`);
      $('.h1', row).textContent = s.title;
      $('.h2 span', row).textContent = s.modified + ' · ' + plural(s.turns, 'message');
      row.onclick = (e) => {
        if (e.target.closest('.ibtn')) { S.sessionId = s.id; showTab('terminal'); startTerm(true); return; }
        send('resume', { sessionId: s.id });
      };
      v.appendChild(row);
    });
  }

  function replay(lines, sessionId) {
    resetChat();
    S.sessionId = sessionId;
    addNote('Resumed chat. Your next message continues it.');
    lines.forEach((raw) => {
      let m; try { m = JSON.parse(raw); } catch (e) { return; }
      const msg = m.message || {};
      if (m.type === 'user') {
        if (typeof msg.content === 'string') { if (!msg.content.startsWith('<')) addUser(msg.content); return; }
        (msg.content || []).forEach((b) => {
          if (b.type === 'text' && b.text && !b.text.startsWith('<command') && !b.text.startsWith('<local-command')) addUser(b.text);
          else if (b.type === 'tool_result') toolResult(b.tool_use_id, b.content, b.is_error);
        });
      } else if (m.type === 'assistant') onAssistant(m);
    });
    $$('.act .run').forEach((n) => n.closest('.st') && (n.closest('.st').innerHTML = icon('check', 'ok')));
    showTab('chat');
    scrollDown(true);
  }

  // ------------------------------------------------------------------ overlays
  function overlay(title, bodyHtml, opts) {
    const o = $('#overlay');
    o.className = 'overlay' + (opts && opts.cls ? ' ' + opts.cls : '');
    o.innerHTML = '';
    if (title) {
      const head = h(`<div class="oh"><span></span><span class="sp"></span><button class="ibtn" title="Close">${icon('x')}</button></div>`);
      $('span', head).textContent = title;
      $('button', head).onclick = closeOverlay;
      o.appendChild(head);
    }
    const body = h(`<div class="ob">${bodyHtml}</div>`);
    o.appendChild(body);
    o.classList.remove('hidden');
    return body;
  }
  function closeOverlay() { $('#overlay').classList.add('hidden'); $('#overlay').innerHTML = ''; }

  function showWelcome() {
    const a = S.auth || {};
    const notInstalled = a.installed === false;
    const b = overlay(null, `
      <img class="logo" src="img/logo.png" alt="">
      <h1>Welcome to CAD Master</h1>
      <p>Claude Code inside SOLIDWORKS. ${notInstalled ? 'Claude Code isn\'t installed on this PC yet.' : 'Sign in with your own Claude account to start.'}</p>
      <div class="btns"></div>
      <p style="font-size:12px">${notInstalled ? 'This runs the official installer from Anthropic in the Terminal tab.' : 'Sign-in opens your browser. If it shows a code, paste it into the Terminal tab.'}</p>`, { cls: 'welcome' });
    const btns = $('.btns', b);
    const mk = (label, cls, fn, ic) => { const x = h(`<button class="btn ${cls}">${ic ? icon(ic) : ''}${esc(label)}</button>`); x.onclick = fn; btns.appendChild(x); };
    if (notInstalled) mk('Install Claude Code', 'pri', () => { closeOverlay(); showTab('terminal'); ensureTerm(); fitTerm(); send('install_claude'); }, 'download');
    else {
      mk('Sign in with Claude', 'pri', () => login('claudeai'), 'login');
      mk('Anthropic Console account', '', () => login('console'));
      mk('Single sign-on (SSO)', '', () => login('sso'));
    }
    mk('Check again', '', () => send('auth_status'), 'refresh');
  }

  function login(method) {
    closeOverlay();
    showTab('terminal');
    ensureTerm(); fitTerm();
    S.term.reset();
    send('auth_login', { method });
    toast('ok', 'Finish signing in in your browser');
  }

  function showAccount() {
    const a = S.auth || {};
    const st = a.status || {};
    const rows = Object.entries(st).filter(([k, v]) => !/directory|analytics/i.test(k) && v !== null && typeof v !== 'object')
      .map(([k, v]) => `<div>${esc(k.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase()))}</div><div>${esc(v)}</div>`).join('');
    const b = overlay('Account', `
      <div class="section">Claude account</div>
      <div class="kv">${rows || '<div>Status</div><div>' + (a.installed === false ? 'Claude Code not installed' : 'Unknown') + '</div>'}</div>
      <div class="section">Claude Code</div>
      <div class="kv"><div>Version</div><div>${esc(a.version || '-')}</div><div>Program</div><div style="word-break:break-all">${esc(a.exe || '-')}</div></div>
      <div class="btns" style="display:flex;flex-wrap:wrap;gap:6px"></div>
      <p class="sub">CAD Master never sees your password or tokens. Claude Code stores them itself.</p>`);
    const btns = $('.btns', b);
    const mk = (label, cls, fn) => { const x = h(`<button class="btn ${cls}">${esc(label)}</button>`); x.onclick = fn; btns.appendChild(x); };
    if (a.installed === false) mk('Install Claude Code', 'pri', () => { closeOverlay(); showTab('terminal'); ensureTerm(); fitTerm(); send('install_claude'); });
    else if (a.loggedIn) { mk('Switch account', '', () => login('claudeai')); mk('Sign out', 'danger', () => { send('auth_logout'); closeOverlay(); }); }
    else { mk('Sign in with Claude', 'pri', () => login('claudeai')); mk('Console account', '', () => login('console')); mk('SSO', '', () => login('sso')); }
    mk('Refresh', '', () => send('auth_status'));
  }

  function showSettings() {
    const st = S.settings || {};
    const opt = (list, cur) => list.map(([v, l]) => `<option value="${esc(v)}" ${String(v) === String(cur) ? 'selected' : ''}>${esc(l)}</option>`).join('');
    const b = overlay('Settings', `
      <div class="section">Claude Code</div>
      <div class="field"><label>Claude Code program</label><input type="text" id="sClaude" placeholder="Found automatically"><div class="hint">Leave empty to find it automatically. Now using: ${esc((S.auth && S.auth.exe) || 'not found')}</div></div>
      <div class="field"><label>Workspace (where Claude saves scripts and exports)</label><select id="sWsMode">${opt([['activeDoc', 'Folder of the active document'], ['fixed', 'Always this folder']], st.WorkspaceMode)}</select>
        <div class="inline" style="margin-top:5px"><input type="text" id="sWs"><button class="btn" id="sBrowse">Browse…</button></div></div>
      <div class="field"><label>New chats start in</label><select id="sMode">${opt(MODES.filter((m) => m.id !== 'bypassPermissions' || st.AllowBypass).map((m) => [m.id, m.label]), st.DefaultMode)}</select></div>
      <label class="check"><input type="checkbox" id="sPlanFirst"><span>Plan first: new chats and design briefs start in Plan mode</span></label>
      <div class="field"><label>Default model</label><select id="sModel">${opt(MODELS, st.DefaultModel)}</select></div>
      <div class="field"><label>Default effort</label><select id="sEffort">${opt(EFFORTS, st.DefaultEffort || '')}</select></div>
      <label class="check"><input type="checkbox" id="sCtx"><span>Send the active document and selection with each message</span></label>
      <label class="check"><input type="checkbox" id="sThink"><span>Show Claude's thinking</span></label>
      <div class="field"><label>Extra instructions for Claude</label><textarea id="sExtra" placeholder="For example: our printers are Bambu X1C; default material PETG; part numbers ABC-000-00"></textarea></div>
      <div class="section">Look</div>
      <div class="field"><label>Theme</label><select id="sTheme">${opt([['dark', 'Dark'], ['light', 'Light'], ['system', 'Follow Windows']], st.Theme)}</select></div>
      <div class="field"><label>Text size</label><select id="sFont">${opt([[12, 'Small'], [13, 'Normal'], [14, 'Large'], [16, 'Extra large']], st.FontSize)}</select></div>
      <div class="section">Safety</div>
      <label class="check"><input type="checkbox" id="sBypass"><span>Bypass mode is on: Claude may take the design decisions and build without asking. It never closes or overwrites your own documents.</span></label>
      <div class="btns" style="display:flex;gap:6px"><button class="btn pri" id="sSave">Save</button><button class="btn" id="sLogs">Open logs</button></div>`);
    $('#sClaude', b).value = st.ClaudePath || '';
    $('#sWs', b).value = st.WorkspaceFolder || '';
    $('#sPlanFirst', b).checked = st.PlanFirst !== false;
    $('#sCtx', b).checked = st.AutoAttachContext !== false;
    $('#sThink', b).checked = st.ShowThinking !== false;
    $('#sExtra', b).value = st.ExtraInstructions || '';
    $('#sBypass', b).checked = !!st.AllowBypass;
    $('#sBrowse', b).onclick = () => send('pick_workspace');
    $('#sLogs', b).onclick = () => send('open_logs');
    $('#sTheme', b).onchange = (e) => { document.documentElement.dataset.theme = e.target.value; };
    $('#sSave', b).onclick = () => {
      const next = Object.assign({}, st, {
        ClaudePath: $('#sClaude', b).value.trim(), WorkspaceMode: $('#sWsMode', b).value, WorkspaceFolder: $('#sWs', b).value.trim(),
        DefaultMode: $('#sMode', b).value, PlanFirst: $('#sPlanFirst', b).checked, DefaultModel: $('#sModel', b).value,
        DefaultEffort: $('#sEffort', b).value, AutoAttachContext: $('#sCtx', b).checked, ShowThinking: $('#sThink', b).checked,
        ExtraInstructions: $('#sExtra', b).value, Theme: $('#sTheme', b).value, FontSize: Number($('#sFont', b).value), AllowBypass: $('#sBypass', b).checked,
      });
      send('save_settings', { settings: next });
      closeOverlay();
    };
  }

  function showBrief() {
    const b = overlay('New design brief', `
      <p class="sub">Claude studies your model, asks what's unclear and writes a plan. Nothing is built until you approve it.</p>
      <div class="field"><label>What are you designing?</label><textarea id="bWhat" placeholder="A wall bracket for a handheld camera, 2 screw holes, slides onto the handle"></textarea></div>
      <div class="field"><label>Size limits and key dimensions</label><input type="text" id="bSize" placeholder="Max 80 × 50 × 30 mm; fits the 38 mm handle"></div>
      <div class="field"><label>Material and process</label><select id="bProc">
        <option>3D printed, PETG</option><option>3D printed, ASA</option><option>3D printed, PLA</option><option>CNC machined aluminium</option><option>Sheet metal</option><option>Injection moulded</option><option>Not decided</option></select></div>
      <div class="field"><label>Interfaces and mating parts</label><input type="text" id="bIf" placeholder="M3 screws into heat-set inserts; mates with the camera handle part"></div>
      <div class="field"><label>Must-haves and checks</label><textarea id="bMust" placeholder="No supports when printed; 2 mm walls; drawing with tolerances"></textarea></div>
      <label class="check"><input type="checkbox" id="bCtx" checked><span>Use the open SOLIDWORKS document as context</span></label>
      <div class="btns" style="display:flex;gap:6px"><button class="btn pri" id="bGo">${icon('clipboard-list')}Plan it</button></div>`);
    $('#bWhat', b).focus();
    $('#bGo', b).onclick = () => {
      const what = $('#bWhat', b).value.trim();
      if (!what) { $('#bWhat', b).focus(); toast('warn', 'Describe what you are designing first'); return; }
      const lines = [
        'New design brief. Plan it before building anything.',
        '', 'What: ' + what,
        $('#bSize', b).value.trim() && 'Size limits: ' + $('#bSize', b).value.trim(),
        'Material and process: ' + $('#bProc', b).value,
        $('#bIf', b).value.trim() && 'Interfaces: ' + $('#bIf', b).value.trim(),
        $('#bMust', b).value.trim() && 'Must-haves: ' + $('#bMust', b).value.trim(),
        $('#bCtx', b).checked ? 'Use the open SOLIDWORKS document (sw_status, sw_feature_tree, sw_screenshot) as context.' : 'Do not use the open document.',
        '', 'Load the solidworks-api skill first. Ask me about anything unclear, then write a numbered plan: parameters and fit table, test runs, feature groups, checks and outputs. Each step must be a task you can verify.',
      ].filter((x) => x !== '' && x !== false && x != null || x === '');
      closeOverlay();
      newChat(true);
      const slash = S.slash.find((s) => s === 'cad:plan' || s === 'cad-plan');
      submit((slash ? '/' + slash + ' ' : '') + lines.join('\n'));
    };
  }

  function showAbout() {
    const a = S.auth || {};
    const skill = S.init ? ((S.init.skills || []).includes('solidworks-api') ? 'found' : 'NOT FOUND') : 'checked when a chat starts';
    const mcp = S.init ? ((S.init.mcp_servers || []).find((s) => s.name === 'solidworks') || {}).status || 'not connected' : 'checked when a chat starts';
    overlay('About CAD Master', `
      <div style="text-align:center"><img src="img/logo.png" alt="" style="width:96px;height:96px;border-radius:50%;background:#fff"></div>
      <div class="kv">
        <div>CAD Master</div><div>${esc(S.version)}</div>
        <div>Claude Code</div><div>${esc(a.version || '-')}</div>
        <div>SOLIDWORKS</div><div>${esc(S.swVersion || (S.swConnected ? 'connected' : 'not connected'))}</div>
        <div>Workspace</div><div style="word-break:break-all">${esc(S.workspace)}</div>
        <div>solidworks-api skill</div><div>${esc(skill)}</div>
        <div>SOLIDWORKS tools (MCP)</div><div>${esc(mcp)}</div>
      </div>
      <div class="section">Tips</div>
      <div class="md"><ul>
        <li><b>Plan first:</b> describe the part, approve Claude's plan, then watch the Tasks tab.</li>
        <li><b>Shift+Tab</b> cycles modes; <b>/</b> opens commands; <b>Esc</b> stops Claude.</li>
        <li>Attach a <b>view</b> or your <b>selection</b> with the camera and pointer buttons.</li>
        <li>The <b>Terminal</b> tab is the full Claude Code: every slash command and setting.</li>
        <li>Drag the task pane's edge to make it wider, or pop it out with ${icon('arrows-diagonal')}.</li>
      </ul></div>`);
  }

  function lightbox(src) { const l = $('#lightbox'); $('img', l).src = src; l.classList.remove('hidden'); }

  // ------------------------------------------------------------------ chat lifecycle
  function resetChat() {
    S.tools = {}; S.stream = null; S.transcript = []; S.driving.clear(); S.alwaysAllow.clear(); S.turnShots = []; S.lastShot = null;
    renderActivity();
    msgs().innerHTML = '';
    renderDetailsEmpty();
  }
  function newChat(design) {
    resetChat();
    S.todos = []; S.plan = null; S.sessionId = null;
    renderTasks();
    renderEmpty();
    send('new_chat', { design: !!design });
    showTab('chat');
  }
  function exportChat() {
    if (!S.transcript.length) { toast('warn', 'Nothing to export yet'); return; }
    const mdText = '# CAD Master chat\n\n' + S.transcript.map((t) => `**${t.role}:**\n\n${t.text}`).join('\n\n---\n\n');
    send('export_chat', { markdown: mdText, name: 'CAD Master chat ' + new Date().toISOString().slice(0, 10) });
  }

  // ------------------------------------------------------------------ messages from the add-in
  bridge.on((m) => {
    switch (m.type) {
      case 'hello':
        S.version = m.version; S.settings = m.settings || {}; S.workspace = m.workspace;
        S.mode = m.mode || S.mode; S.model = m.model || 'default'; S.effort = m.effort || '';
        S.swConnected = !!m.swConnected;
        S.swVersion = m.sw && m.sw.solidworks;
        applySettings(); renderChips(); renderHeader(); renderStatus(); renderEmpty();
        break;
      case 'auth':
        S.auth = m; renderHeader(); renderStatus();
        if (!m.loggedIn) showWelcome();
        else if ($('#overlay.welcome')) { closeOverlay(); toast('ok', 'Signed in'); showTab('chat'); }
        break;
      case 'context': S.ctx = { title: m.title, path: m.path, selected: m.selected }; renderHeader(); renderCtxChips(); break;
      case 'claude': onClaude(m.msg); break;
      case 'permission': onPermission(m); break;
      case 'session':
        if (m.workspace) S.workspace = m.workspace;
        if (m.sessionId) S.sessionId = m.sessionId;
        if (m.mode) S.mode = m.mode;
        if (m.state === 'busy') { if (!S.busy) startTurn(); S.busy = true; }
        if (m.state === 'idle' || m.state === 'stopped' || m.state === 'none') S.busy = false;
        if (m.state === 'stopped') {
          $$('.act .run').forEach((n) => n.closest('.st') && (n.closest('.st').innerHTML = icon('circle')));
          S.driving.clear();
          if (m.error) { addNote('Claude stopped unexpectedly. The Details tab has the technical message.', true); addDetail(h('<div class="note err"></div>')).textContent = 'Claude Code stopped:\n' + m.error; }
          else addNote('Stopped');
        }
        renderChips(); renderHeader(); renderActivity();
        break;
      case 'mode': S.mode = m.mode; renderChips(); break;
      case 'settings': S.settings = m.settings; applySettings(); renderCtxChips(); break;
      case 'attachment': S.attachments.push(m); renderCtxChips(); showTab('chat'); $('#input').focus(); break;
      case 'sessions': renderHistory(m.list, m.workspace); break;
      case 'transcript': replay(m.lines || [], m.sessionId); break;
      case 'toast': toast(m.level, m.text); break;
      case 'term_started': ensureTerm(); $('#termTitle').textContent = m.title || ''; if (S.tab !== 'terminal') showTab('terminal'); break;
      case 'term_data': ensureTerm(); S.term.write(m.data); break;
      case 'term_exit':
        if (S.term) S.term.writeln('\r\n\x1b[90m[' + (m.title || 'process') + ' ended]\x1b[0m');
        $('#termTitle').textContent = '';
        if (m.title === 'Sign in' || m.title === 'Install') send('auth_status');
        break;
      case 'command': onCommand(m.id); break;
    }
  });

  function onCommand(id) {
    switch (id) {
      case 'open': showTab('chat'); break;
      case 'new_chat': newChat(false); break;
      case 'new_design': showBrief(); break;
      case 'ask_selection': showTab('chat'); send('attach_selection'); if (!$('#input').value) $('#input').value = 'About the selected geometry: '; break;
      case 'send_view': showTab('chat'); send('attach_view', { view: 'current' }); break;
      case 'attach_file': send('attach_file'); break;
      case 'review': case 'drawing': case 'checks': case 'export': runCad(id); break;
      case 'mode': showTab('chat'); modeMenu($('#modeChip')); break;
      case 'tasks': showTab('tasks'); break;
      case 'history': showTab('history'); break;
      case 'terminal': showTab('terminal'); break;
      case 'account': showAccount(); break;
      case 'settings': showSettings(); break;
      case 'help': showAbout(); break;
    }
  }

  // ------------------------------------------------------------------ wiring
  hydrate(document);
  $$('.tabs .tab').forEach((b) => b.onclick = () => showTab(b.dataset.tab));
  $('#newChatBtn').onclick = () => newChat(false);
  $('#menuBtn').onclick = (e) => mainMenu(e.currentTarget);
  $('#avatarBtn').onclick = showAccount;
  $('#popoutBtn').onclick = () => send('popout');
  $('#modeChip').onclick = (e) => modeMenu(e.currentTarget);
  $('#modelChip').onclick = (e) => modelMenu(e.currentTarget);
  $('#effortChip').onclick = (e) => effortMenu(e.currentTarget);
  $('#viewBtn').onclick = (e) => openMenu(e.currentTarget, ['current', 'isometric', 'front', 'top', 'right'].map((v) => ({ label: v === 'current' ? 'Current view' : v[0].toUpperCase() + v.slice(1) + ' view', icon: 'camera', run: () => send('attach_view', { view: v }) })));
  $('#selBtn').onclick = () => send('attach_selection');
  $('#fileBtn').onclick = () => send('attach_file');
  $('#sendBtn').onclick = () => (S.busy && !$('#input').value.trim() ? send('interrupt') : submit());
  $('#termNew').onclick = () => startTerm(false);
  $('#termResume').onclick = () => startTerm(true);
  $('#termStop').onclick = () => send('term_stop');
  $('#lightbox').onclick = () => $('#lightbox').classList.add('hidden');
  $('#actDetails').onclick = () => showTab('details');
  $('#activity .thumb').onclick = (e) => lightbox(e.target.src);
  document.addEventListener('click', (e) => {
    if (e.target.matches('img.shot')) { lightbox(e.target.src); return; }
    const chip = e.target.closest('.fchip');
    if (chip) fileMenu(chip);
  });
  setInterval(() => { if (S.busy) renderActivity(); }, 1000);

  // Drag out: file chips and model views go to Explorer, the desktop or an email as real files.
  document.addEventListener('dragstart', (e) => {
    const chip = e.target.closest && e.target.closest('.fchip');
    if (chip) { e.preventDefault(); send('drag_out', { path: chip.dataset.path }); return; }
    const img = e.target.closest && e.target.closest('img.shot');
    if (img && img.src.startsWith('data:')) {
      e.preventDefault();
      const [head, data] = img.src.split(',');
      send('drag_out', { data, mediaType: head.slice(5).split(';')[0], name: img.dataset.name || 'model view' });
    }
  });
  // Drag in: pictures, sketches and CAD files dropped anywhere on the panel are attached to the next message.
  let dragDepth = 0;
  const outsideDrag = (e) => Array.from((e.dataTransfer && e.dataTransfer.types) || []).some((t) => t === 'Files' || t === 'text/uri-list');
  const dropZone = (on) => $('#dropzone').classList.toggle('hidden', !on);
  document.addEventListener('dragenter', (e) => { if (outsideDrag(e)) { dragDepth++; dropZone(true); } });
  document.addEventListener('dragleave', (e) => { if (outsideDrag(e) && --dragDepth <= 0) { dragDepth = 0; dropZone(false); } });
  document.addEventListener('dragover', (e) => { if (outsideDrag(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
  document.addEventListener('drop', (e) => {
    if (!outsideDrag(e)) return;
    e.preventDefault();
    dragDepth = 0; dropZone(false);
    const files = Array.from(e.dataTransfer.files || []);
    const wv = window.chrome && window.chrome.webview;
    if (files.length && wv && wv.postMessageWithAdditionalObjects) wv.postMessageWithAdditionalObjects({ type: 'drop_files' }, files);
    else if (files.length) files.forEach(attachLocalFile);
    else {
      const uri = (e.dataTransfer.getData('text/uri-list') || '').split('\n')[0].trim();
      if (uri) { S.attachments.push({ kind: 'text', name: uri.split('/').pop().slice(0, 40) || 'link', text: 'Reference link: ' + uri }); renderCtxChips(); }
    }
    showTab('chat');
    $('#input').focus();
  });
  // Without the add-in (browser preview) files are read here; images only.
  function attachLocalFile(f) {
    if (!f.type.startsWith('image/')) { S.attachments.push({ kind: 'text', name: f.name, text: 'Attached file: ' + f.name }); renderCtxChips(); return; }
    const r = new FileReader();
    r.onload = () => { S.attachments.push({ kind: 'image', mediaType: f.type, name: f.name, data: String(r.result).split(',')[1] }); renderCtxChips(); };
    r.readAsDataURL(f);
  }

  const input = $('#input');
  input.addEventListener('input', () => { autosize(); updatePalette(); });
  input.addEventListener('keydown', (e) => {
    const pal = $('#palette');
    if (!pal.classList.contains('hidden') && (e.key === 'Tab' || (e.key === 'Enter' && !e.shiftKey))) {
      const first = $('.mi', pal);
      if (first) { e.preventDefault(); first.click(); return; }
    }
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit(); }
    else if (e.key === 'Tab' && e.shiftKey) {
      e.preventDefault();
      const list = MODES.filter((m) => m.id !== 'bypassPermissions' || S.settings.AllowBypass).map((m) => m.id); // Shift+Tab never turns Bypass on by surprise
      setMode(list[(list.indexOf(S.mode) + 1) % list.length]);
    } else if (e.key === 'ArrowUp' && !input.value && S.lastInput) { input.value = S.lastInput; e.preventDefault(); }
    else if (e.key === 'Escape') { pal.classList.add('hidden'); if (S.busy) send('interrupt'); }
  });
  input.addEventListener('paste', (e) => {
    const items = Array.from((e.clipboardData || {}).items || []).filter((i) => i.type.startsWith('image/'));
    items.forEach((it) => {
      const f = it.getAsFile();
      const r = new FileReader();
      r.onload = () => { S.attachments.push({ kind: 'image', mediaType: f.type, name: 'Pasted image', data: String(r.result).split(',')[1] }); renderCtxChips(); };
      r.readAsDataURL(f);
    });
  });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') { closeMenus(); $('#lightbox').classList.add('hidden'); } });

  window.CADMASTER_STATE = S; // read by tests\cdp.js
  renderChips(); renderHeader(); renderEmpty(); renderDetailsEmpty();
  if (bridge.mock && window.installMock) window.installMock();
  send('ready');
})();
