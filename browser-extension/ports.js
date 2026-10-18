"use strict";

(function (root) {
  function localhostPort(value) {
    try {
      const url = new URL(value);
      if (!["http:", "https:"].includes(url.protocol)) return null;
      if (!["localhost", "127.0.0.1", "[::1]"].includes(url.hostname)) return null;
      if (url.username || url.password) return null;
      return Number(url.port || (url.protocol === "https:" ? 443 : 80));
    } catch {
      return null;
    }
  }

  function openPorts(tabs) {
    const ports = new Set();
    for (const tab of tabs) {
      // Count background, frozen and discarded tabs too. They are not closed.
      for (const url of [tab.url, tab.pendingUrl]) {
        const port = localhostPort(url);
        if (port && port !== 47832) ports.add(port);
      }
    }
    return [...ports].sort((a, b) => a - b);
  }

  root.LocalPreviewPorts = { localhostPort, openPorts };
  if (typeof module !== "undefined") module.exports = root.LocalPreviewPorts;
})(globalThis);
