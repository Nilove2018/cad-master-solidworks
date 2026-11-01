// Preview mode: when the page is opened in a normal browser (no WebView2 host), this fakes the add-in
// so the UI can be checked without SOLIDWORKS or Claude. Not used inside SOLIDWORKS.
window.installMock = function () {
  const emit = (m) => setTimeout(() => window.bridge.emit(m), 0);
  const later = (ms, m) => setTimeout(() => window.bridge.emit(m), ms);
  const claude = (msg) => ({ type: 'claude', msg });
  let turn = 0;
  const cv = document.createElement('canvas');
  cv.width = 320; cv.height = 200;
  const g = cv.getContext('2d');
  g.fillStyle = '#F4F6FA'; g.fillRect(0, 0, 320, 200);
  g.fillStyle = '#8AA0C8'; g.fillRect(110, 60, 100, 90);
  g.fillStyle = '#5B739E'; g.beginPath(); g.ellipse(160, 60, 50, 16, 0, 0, 7); g.fill();
  window.MOCK_SHOT = cv.toDataURL('image/png').split(',')[1];
  const settings = { Theme: (new URLSearchParams(location.search)).get('theme') || 'dark', FontSize: 13, PlanFirst: true, AutoAttachContext: true, ShowThinking: true, WorkspaceMode: 'activeDoc', WorkspaceFolder: 'C:\\Users\\you\\Documents\\CAD Master', DefaultMode: 'default', DefaultModel: 'default', DefaultEffort: '' };

  // Setup checklist: ?setup=new shows a fresh PC (things missing), anything else a finished setup.
  const freshPc = (new URLSearchParams(location.search)).get('setup') === 'new';
  const have = freshPc ? { claude: true, signin: true, python: false, pywin32: false, git: true, skill: false, swtest: false } : { claude: true, signin: true, python: true, pywin32: true, git: true, skill: true, swtest: false };
  const setupItems = () => [
    have.claude ? { id: 'claude', title: 'Claude Code', required: true, state: 'ok', detail: 'Version 2.1.309' } : { id: 'claude', title: 'Claude Code', required: true, state: 'missing', fix: 'install_claude', fixLabel: 'Install Claude Code', detail: 'The engine behind CAD Master, from Anthropic.' },
    have.signin ? { id: 'signin', title: 'Your Claude account', required: true, state: 'ok', detail: 'you@example.com · Pro plan' } : { id: 'signin', title: 'Your Claude account', required: true, state: 'missing', fix: 'sign_in', fixLabel: 'Sign in', detail: 'Sign in with your own Claude account.' },
    have.python ? { id: 'python', title: 'Python', required: true, state: 'ok', detail: 'Python 3.12.7' } : { id: 'python', title: 'Python', required: true, state: 'missing', fix: 'install_python', fixLabel: 'Install Python', detail: 'Claude writes its SOLIDWORKS build scripts in Python. Installs Python 3.12 for your Windows user.' },
    !have.python ? { id: 'pywin32', title: 'SOLIDWORKS link for Python (pywin32)', required: true, state: 'blocked', detail: 'After Python is installed.' }
      : have.pywin32 ? { id: 'pywin32', title: 'SOLIDWORKS link for Python (pywin32)', required: true, state: 'ok', detail: 'pywin32 311' }
      : { id: 'pywin32', title: 'SOLIDWORKS link for Python (pywin32)', required: true, state: 'missing', fix: 'install_pywin32', fixLabel: 'Install pywin32', detail: 'Lets Python scripts drive SOLIDWORKS through its API.' },
    { id: 'git', title: 'Git for Windows', required: true, state: 'ok', detail: 'Found. Claude can use Bash as well as PowerShell.' },
    have.skill ? { id: 'skill', title: 'Design rules (solidworks-api skill)', required: true, state: 'ok', detail: 'Version 2.3.2, installed by CAD Master' } : { id: 'skill', title: 'Design rules (solidworks-api skill)', required: true, state: 'missing', fix: 'install_skill', fixLabel: 'Install design rules', detail: 'How Claude builds correct SOLIDWORKS models. Updates itself.' },
    have.swtest ? { id: 'swtest', title: 'SOLIDWORKS test', state: 'ok', fix: 'run_sw_test', fixLabel: 'Run again', detail: 'Passed: built and checked the test block in 14 s.' }
      : { id: 'swtest', title: 'SOLIDWORKS test', state: 'untested', fix: 'run_sw_test', fixLabel: 'Run test', detail: 'Builds a 40 × 20 × 10 mm test block in a new part, checks it and closes it without saving. About 20 seconds.' },
  ];
  const emitSetup = () => { const items = setupItems(); emit({ type: 'setup', items, ready: items.filter((i) => i.required).every((i) => i.state === 'ok'), done: !freshPc && settings.SetupDone }); };
  const FIXES = { install_python: ['python', 'Python is installed'], install_pywin32: ['pywin32', 'pywin32 is installed'], install_skill: ['skill', 'Design rules installed'], run_sw_test: ['swtest', 'SOLIDWORKS test passed'], install_claude: ['claude', 'Claude Code is installed'] };

  window.mockHost = function (msg) {
    switch (msg.type) {
      case 'ready':
        emit({ type: 'hello', version: '1.0.0', settings, workspace: 'C:\\Users\\you\\Documents\\CAD Master\\Camera Mount', mode: 'plan', model: 'default', effort: '', swConnected: true, sw: { solidworks: 'SOLIDWORKS 33.1.2' } });
        emit({ type: 'auth', installed: true, loggedIn: true, version: '2.1.309 (Claude Code)', exe: 'C:\\Users\\you\\.local\\bin\\claude.exe', status: { loggedIn: true, authMethod: 'claude.ai', email: 'you@example.com', subscriptionType: 'pro' } });
        emit({ type: 'context', title: 'CM-100 Camera Mount.SLDASM', path: 'C:\\Users\\you\\Documents\\CAD Master\\Camera Mount\\CM-100 Camera Mount.SLDASM', selected: 1 });
        if (freshPc) setTimeout(emitSetup, 300); // a finished setup stays out of the way of the UI tests
        break;
      case 'setup_check': setTimeout(emitSetup, 400); break;
      case 'setup_fix': {
        const [key, text] = FIXES[msg.id] || [];
        emit({ type: 'setup_fix', id: msg.id, state: 'running' });
        // ?fail=install_pywin32 makes that fix fail, to see the error box
        const fail = (new URLSearchParams(location.search)).get('fail') === msg.id;
        setTimeout(() => {
          if (!fail && key) have[key] = true;
          emit(fail ? { type: 'setup_fix', id: msg.id, state: 'fail', message: 'pip couldn\'t install pywin32', log: 'ERROR: Could not find a version that satisfies the requirement pywin32\nERROR: No matching distribution found for pywin32' }
                    : { type: 'setup_fix', id: msg.id, state: 'ok', message: text });
          emitSetup();
        }, msg.id === 'run_sw_test' ? 2500 : 1500);
        break;
      }
      case 'setup_done': settings.SetupDone = true; emit({ type: 'settings', settings }); break;
      case 'send': {
        turn++;
        const id = 'mock' + turn;
        emit({ type: 'session', state: 'busy', sessionId: 'mock-session' });
        emit(claude({ type: 'system', subtype: 'init', model: 'claude-opus-5-5', permissionMode: 'plan', slash_commands: ['compact', 'context', 'cad:plan', 'cad:review', 'cad:check', 'cad:drawing', 'cad:export', 'cad:testrun', 'solidworks-api'], skills: ['solidworks-api'], mcp_servers: [{ name: 'solidworks', status: 'connected' }] }));
        later(150, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: id + 's', name: 'Skill', input: { skill: 'solidworks-api' } }] } }));
        later(300, claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: id + 's', content: 'Launching skill: solidworks-api' }] } }));
        later(450, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: id + 't', name: 'mcp__solidworks__sw_feature_tree', input: {} }] } }));
        later(700, claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: id + 't', content: [{ type: 'text', text: '{"document":"CM-100 Camera Mount.SLDASM","featureCount":58,"components":41}' }] }] } }));
        const text = 'The handle floor of `CM-101 Handle` is a **2.0 mm** PETG wall. A ¼″-20 insert needs 9.5 mm, so I\'ll add a ribbed boss.\n\nNothing changes until you approve the plan.';
        later(800, claude({ type: 'stream_event', event: { type: 'message_start' } }));
        later(820, claude({ type: 'stream_event', event: { type: 'content_block_start', index: 0, content_block: { type: 'text' } } }));
        for (let i = 0; i < text.length; i += 12) later(850 + i * 4, claude({ type: 'stream_event', event: { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: text.slice(i, i + 12) } } }));
        later(850 + text.length * 4 + 50, claude({ type: 'assistant', message: { content: [{ type: 'text', text }] } }));
        const plan = '# Tripod mount for CM-101 Handle\n\n1. Back up the handle part and ray-scan the floor\n2. Parameters: insert Ø8.0 × 9.5 mm, boss Ø14, 0.4 mm clearance\n3. Test run: one boss on a scratch copy\n4. Build group TRIPOD-1: boss, 4 ribs, hole\n5. Interference with the USB board, What\'s Wrong 0\n6. Render, export STEP and STL';
        later(900 + text.length * 4 + 200, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: id + 'p', name: 'ExitPlanMode', input: { plan } }] } }));
        later(900 + text.length * 4 + 300, { type: 'permission', requestId: 'r' + id, request: { tool_name: 'ExitPlanMode', input: { plan }, tool_use_id: id + 'p' } });
        break;
      }
      case 'permission_response':
        if (msg.response.behavior === 'allow') {
          const todos = ['Back up the handle part and ray-scan the floor', 'Parameters: insert Ø8.0 × 9.5 mm, boss Ø14', 'Test run: one boss on a scratch copy', 'Build group TRIPOD-1: boss, 4 ribs, hole', 'Interference with the USB board, What\'s Wrong 0', 'Render, export STEP and STL'];
          const mk = (n) => todos.map((c, i) => ({ content: c, activeForm: c, status: i < n ? 'completed' : i === n ? 'in_progress' : 'pending' }));
          emit(claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'mock' + turn + 'p', content: 'User approved the plan' }] } }));
          emit(claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'td1', name: 'TodoWrite', input: { todos: mk(0) } }] } }));
          const T = Number((new URLSearchParams(location.search)).get('slow') || 1);
          later(400 * T, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'b1', name: 'Bash', input: { command: 'cd Scripts && python -P build_tripod.py --group TRIPOD-1', description: 'Sketching the boss and ribs' } }] } }));
          later(1600 * T, claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'b1', content: 'TRIPOD-1 built: boss, 4 ribs, hole\nvolume +1.84 cm3, 1 body, What\'s Wrong 0' }] } }));
          later(1650 * T, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'sh1', name: 'mcp__solidworks__sw_screenshot', input: { view: 'isometric' } }] } }));
          later(1700 * T, claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'sh1', content: [{ type: 'image', source: { type: 'base64', media_type: 'image/png', data: window.MOCK_SHOT || '' } }] }] } }));
          later(1750 * T, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'td2', name: 'TodoWrite', input: { todos: mk(3) } }] } }));
          later(1800 * T, claude({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'b2', name: 'Bash', input: { command: 'cd Scripts && python -P check_tripod.py', description: 'Run python check script' } }] } }));
          later(2600 * T, claude({ type: 'user', message: { content: [{ type: 'tool_result', tool_use_id: 'b2', content: 'interference 0, whatsWrong 0' }] } }));
          later(2700 * T, claude({ type: 'assistant', message: { content: [{ type: 'text', text: 'TRIPOD-1 is built and checked: Ø14 mm boss with four 1.6 mm ribs, no interference with the USB board, What\'s Wrong 0.\n\nFiles: `C:\\Users\\you\\Documents\\CAD Master\\Outputs\\Tripod\\TRIPOD-1.SLDPRT` and C:\\Users\\you\\Documents\\Exports\\TRIPOD-1.step' }] } }));
          later(2800 * T, claude({ type: 'result', subtype: 'success', duration_ms: 48200, num_turns: 9, usage: { input_tokens: 1200, output_tokens: 2400, cache_read_input_tokens: 31000 } }));
          later(2800 * T, { type: 'session', state: 'idle' });
        } else {
          emit(claude({ type: 'result', subtype: 'success', duration_ms: 1200, num_turns: 1, usage: {} }));
          emit({ type: 'session', state: 'idle' });
        }
        break;
      case 'list_sessions':
        emit({ type: 'sessions', workspace: 'C:\\Users\\you\\Documents\\CAD Master\\Camera Mount', list: [
          { id: 'a', title: 'Tripod mount for the camera handle', modified: '2026-10-31 14:20', turns: 12 },
          { id: 'b', title: 'Vent grille review for the rear housing', modified: '2026-10-30 18:05', turns: 31 },
        ] });
        break;
      case 'attach_view':
        emit({ type: 'toast', level: 'ok', text: 'Preview mode: no SOLIDWORKS view to capture' });
        break;
      case 'set_mode': emit({ type: 'mode', mode: msg.mode }); break;
      case 'new_chat': emit({ type: 'session', state: 'none', mode: msg.design ? 'plan' : undefined }); break;
    }
  };
};
