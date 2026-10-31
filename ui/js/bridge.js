// Message bridge between the page and the CAD Master add-in (WebView2).
// Opened in a normal browser (no WebView2), the page runs against a small mock so the UI can be previewed.
(function () {
  const listeners = [];
  const wv = window.chrome && window.chrome.webview;

  window.bridge = {
    mock: !wv,
    send(type, data) {
      const msg = Object.assign({ type }, data || {});
      if (wv) wv.postMessage(msg);
      else if (window.mockHost) window.mockHost(msg);
    },
    on(fn) { listeners.push(fn); },
    emit(msg) { listeners.forEach((fn) => { try { fn(msg); } catch (e) { console.error(e); } }); },
  };

  if (wv) wv.addEventListener('message', (e) => window.bridge.emit(e.data));
})();
