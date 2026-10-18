"use strict";

async function refresh() {
  const status = document.getElementById("status");
  const button = document.getElementById("retry");
  button.disabled = true;
  try {
    await chrome.runtime.sendMessage({ type: "reconcile" });
    const { delivery } = await chrome.storage.local.get("delivery");
    status.textContent = delivery?.ok
      ? `已连接，当前有 ${delivery.ports.length} 个本地预览端口。`
      : "未连接。请先打开 Memory Compression。";
  } catch {
    status.textContent = "连接失败，请重新加载扩展后再试。";
  } finally {
    button.disabled = false;
  }
}

document.getElementById("retry").addEventListener("click", refresh);
refresh();
