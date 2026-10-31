// Spike: drive Claude Code headless through stream-json + the SDK control channel.
// Usage: node spike_control.js <claude.exe> <workdir>
const { spawn } = require('child_process');
const readline = require('readline');

const exe = process.argv[2];
const cwd = process.argv[3];
const args = ['-p', '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose',
  '--permission-prompt-tool', 'stdio', '--model', 'haiku', '--permission-mode', 'default',
  '--include-partial-messages'];
const p = spawn(exe, args, { cwd, stdio: ['pipe', 'pipe', 'pipe'] });
let reqId = 0;
const send = (o) => p.stdin.write(JSON.stringify(o) + '\n');
const control = (request) => { const id = 'req_' + (++reqId); send({ type: 'control_request', request_id: id, request }); return id; };

const seen = {};
readline.createInterface({ input: p.stdout }).on('line', (line) => {
  let m; try { m = JSON.parse(line); } catch { console.log('RAW', line.slice(0, 200)); return; }
  const key = m.type + (m.subtype ? '/' + m.subtype : '');
  seen[key] = (seen[key] || 0) + 1;
  if (m.type === 'stream_event') return;
  if (m.type === 'system' && m.subtype === 'init') {
    console.log('INIT keys:', Object.keys(m).join(','));
    console.log('INIT tools:', (m.tools || []).join(','));
    console.log('INIT model:', m.model, 'mode:', m.permissionMode, 'skills:', JSON.stringify(m.skills || []).slice(0, 300));
    console.log('INIT slash:', JSON.stringify(m.slash_commands || []).slice(0, 300));
    return;
  }
  if (m.type === 'control_request') {
    console.log('CONTROL_REQUEST', JSON.stringify(m).slice(0, 600));
    if (m.request.subtype === 'can_use_tool') {
      send({ type: 'control_response', response: { subtype: 'success', request_id: m.request_id,
        response: { behavior: 'allow', updatedInput: m.request.input } } });
    }
    return;
  }
  if (m.type === 'control_response') { console.log('CONTROL_RESPONSE', JSON.stringify(m).slice(0, 400)); return; }
  if (m.type === 'result') {
    console.log('RESULT', JSON.stringify(m).slice(0, 500));
    step++;
    next();
    return;
  }
  console.log(key, JSON.stringify(m).slice(0, 300));
});
p.stderr.on('data', (d) => console.log('STDERR', d.toString().slice(0, 300)));
p.on('exit', (c) => { console.log('EXIT', c, 'seen', JSON.stringify(seen)); process.exit(0); });

let step = 0;
function next() {
  if (step === 1) {
    control({ subtype: 'set_permission_mode', mode: 'plan' });
    send({ type: 'user', message: { role: 'user', content: [{ type: 'text', text: 'Reply with just the word PLANNED.' }] }, parent_tool_use_id: null, session_id: 'default' });
  } else if (step === 2) {
    send({ type: 'user', message: { role: 'user', content: [{ type: 'text', text: 'Count slowly from 1 to 200, one number per line.' }] }, parent_tool_use_id: null, session_id: 'default' });
    setTimeout(() => control({ subtype: 'interrupt' }), 4000);
  } else {
    p.stdin.end();
  }
}
control({ subtype: 'initialize', hooks: null });
send({ type: 'user', message: { role: 'user', content: [{ type: 'text', text: 'Use the Write tool to create spike.txt containing the word hi. Then say DONE.' }] }, parent_tool_use_id: null, session_id: 'default' });
setTimeout(() => { console.log('TIMEOUT'); p.kill(); }, 180000);
