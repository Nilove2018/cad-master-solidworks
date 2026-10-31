// Drives a page over the Chrome DevTools Protocol (headless Edge, or the CAD Master WebView2 started
// with CADMASTER_DEBUG_PORT) for UI tests: run JavaScript steps, wait, save screenshots.
// Usage: node cdp.js <port> <steps.json>
// steps.json: [{"eval": "js"}, {"wait": 500}, {"shot": "file.png"}, {"print": "js expression"}]
const fs = require('fs');

async function main() {
  const [port, stepsFile] = process.argv.slice(2);
  const steps = JSON.parse(fs.readFileSync(stepsFile, 'utf8'));
  let targets = [];
  for (let i = 0; i < 40; i++) {
    try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); } catch (e) { }
    if (targets.some((t) => t.type === 'page' && /index\.html/.test(t.url))) break;
    await new Promise((r) => setTimeout(r, 500));
  }
  const page = targets.find((t) => t.type === 'page' && /index\.html/.test(t.url));
  if (!page) throw new Error('no CAD Master page on port ' + port);
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  };
  const call = (method, params) => new Promise((r) => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async (expression) => {
    const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.result && r.result.exceptionDetails) throw new Error('JS error: ' + JSON.stringify(r.result.exceptionDetails).slice(0, 500));
    return r.result && r.result.result && r.result.result.value;
  };
  for (const s of steps) {
    if (s.eval) await evaluate(s.eval);
    if (s.wait) await new Promise((r) => setTimeout(r, s.wait));
    if (s.print) console.log(JSON.stringify(await evaluate(s.print)));
    if (s.waitFor) {
      const end = Date.now() + (s.timeout || 60000);
      while (!(await evaluate(s.waitFor))) {
        if (Date.now() > end) { console.log('TIMEOUT waiting for', s.waitFor); break; }
        await new Promise((r) => setTimeout(r, 500));
      }
    }
    if (s.shot) {
      const r = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      fs.mkdirSync(require('path').dirname(s.shot), { recursive: true });
      fs.writeFileSync(s.shot, Buffer.from(r.result.data, 'base64'));
      console.log('saved', s.shot);
    }
  }
  ws.close();
}
main().catch((e) => { console.error(e.message); process.exit(1); });
