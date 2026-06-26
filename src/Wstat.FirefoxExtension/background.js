const SERVER_URL = "http://127.0.0.1:12345/tab";

let pendingTabId = null;

function sendTabInfo(tabId) {
  browser.tabs.get(tabId).then((tab) => {
    if (browser.runtime.lastError || !tab || tab.discarded) return;

    const payload = {
      url: tab.url || "",
      title: tab.title || ""
    };

    fetch(SERVER_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    }).catch(() => {
      // Desktop app unreachable — silently ignore
    });
  }).catch(() => {});
}

browser.tabs.onActivated.addListener((activeInfo) => {
  sendTabInfo(activeInfo.tabId);
});

browser.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.active) {
    sendTabInfo(tabId);
  }
});

// Also send when the window regains focus
browser.windows.onFocusChanged.addListener((windowId) => {
  if (windowId === browser.windows.WINDOW_ID_NONE) return;

  browser.tabs.query({ active: true, currentWindow: true }).then((tabs) => {
    if (tabs && tabs.length > 0) {
      sendTabInfo(tabs[0].id);
    }
  }).catch(() => {});
});
