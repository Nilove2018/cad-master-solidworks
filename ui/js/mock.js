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

  window.mockHost = function (msg) {
    switch (msg.type) {
      case 'ready':
        emit({ type: 'hello', version: '1.0.0', settings, workspace: 'C:\\Users\\you\\Documents\\CAD Master\\Camera Mount', mode: 'plan', model: 'default', effort: '', swConnected: true, sw: { solidworks: 'SOLIDWORKS 33.1.2' } });
        emit({ type: 'auth', installed: true, loggedIn: true, version: '2.1.309 (Claude Code)', exe: 'C:\\Users\\you\\.local\\bin\\claude.exe', status: { loggedIn: true, authMethod: 'claude.ai', email: 'you@example.com', subscriptionType: 'pro' } });
        emit({ type: 'context', title: 'CM-100 Camera Mount.SLDASM', path: 'C:\\Users\\you\\Documents\\CAD Master\\Camera Mount\\CM-100 Camera Mount.SLDASM', selected: 1 });
        break;
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
