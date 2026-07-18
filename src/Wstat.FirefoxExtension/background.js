const PORT = 12345;
const SERVER_URL = `http://127.0.0.1:${PORT}/tab`;

function isHttpUrl(url) {
  return url && (url.startsWith("http://") || url.startsWith("https://"));
}

function sendTabInfo(tabId) {
  browser.tabs.get(tabId).then((tab) => {
    if (browser.runtime.lastError || !tab || tab.discarded) return;
    if (!isHttpUrl(tab.url)) return;

    const payload = {
      url: tab.url,
      title: tab.title || ""
    };

    fetch(SERVER_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    }).catch((err) => {
      console.log("[wstat] Fetch failed:", err.message);
    });
  }).catch((err) => {
    console.log("[wstat] tabs.get failed:", err.message);
  });
}

browser.tabs.onActivated.addListener((activeInfo) => {
  sendTabInfo(activeInfo.tabId);
});

browser.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.active) {
    sendTabInfo(tabId);
  }
});

browser.windows.onFocusChanged.addListener((windowId) => {
  if (windowId === browser.windows.WINDOW_ID_NONE) return;

  browser.tabs.query({ active: true, currentWindow: true }).then((tabs) => {
    if (tabs && tabs.length > 0) {
      sendTabInfo(tabs[0].id);
    }
  }).catch((err) => {
    console.log("[wstat] windows.onFocusChanged error:", err.message);
  });
});
