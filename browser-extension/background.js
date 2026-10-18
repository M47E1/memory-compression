"use strict";

importScripts("ports.js");

const BRIDGE = "http://127.0.0.1:47832/v1/tabs";
let work = Promise.resolve();
let running = false;
let dirty = false;

async function reconcile() {
  const tabs = await chrome.tabs.query({});
  const ports = LocalPreviewPorts.openPorts(tabs);
  const stored = await chrome.storage.local.get(["source_id", "source_sequence"]);
  const sourceId = stored.source_id || crypto.randomUUID();
  const sequence = (stored.source_sequence || 0) + 1;
  await chrome.storage.local.set({ source_id: sourceId, source_sequence: sequence });
  const observation = {
    schema_version: 1,
    source_id: sourceId,
    source_sequence: sequence,
    observed_at_utc: new Date().toISOString(),
    open_ports: ports,
  };
  try {
    const response = await fetch(BRIDGE, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(observation),
      credentials: "omit",
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    if (!response.ok) throw new Error("Bridge rejected observation");
    const result = await response.json();
    if (!["accepted", "duplicate"].includes(result.result)) throw new Error("Unexpected bridge response");
    await chrome.storage.local.set({ delivery: { ok: true, at: new Date().toISOString(), ports } });
    await chrome.action.setBadgeText({ text: ports.length ? String(ports.length) : "" });
    await chrome.action.setBadgeBackgroundColor({ color: "#31644A" });
    return true;
  } catch {
    // This is a current-status projection, not a browsing history. Retry a fresh
    // complete observation; never replay an old close after a newer tab opened.
    await chrome.storage.local.set({ delivery: { ok: false, at: new Date().toISOString(), ports } });
    await chrome.action.setBadgeText({ text: "!" });
    await chrome.action.setBadgeBackgroundColor({ color: "#A84432" });
    return false;
  }
}

function schedule() {
  dirty = true;
  if (running) return work;
  running = true;
  work = (async () => {
    let result = false;
    do {
      dirty = false;
      result = await reconcile();
    } while (dirty);
    return result;
  })().finally(() => { running = false; });
  work.catch(() => {});
  return work;
}

chrome.tabs.onCreated.addListener(schedule);
chrome.tabs.onUpdated.addListener((id, change) => {
  if (change.url || change.status === "complete") schedule();
});
chrome.tabs.onRemoved.addListener(schedule);
chrome.tabs.onReplaced.addListener(schedule);
chrome.runtime.onInstalled.addListener(schedule);
chrome.runtime.onStartup.addListener(schedule);
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "reconcile-localhost") schedule();
});
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message?.type !== "reconcile" || sender.id !== chrome.runtime.id) return false;
  schedule().then((ok) => reply({ ok })).catch(() => reply({ ok: false }));
  return true;
});
chrome.alarms.create("reconcile-localhost", { periodInMinutes: 0.5 });
schedule();
