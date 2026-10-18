"use strict";

const fs = require("node:fs");
const path = require("node:path");
const net = require("node:net");
const crypto = require("node:crypto");
const assert = require("node:assert/strict");
const { spawn, execFileSync } = require("node:child_process");
const root = path.resolve(__dirname, "..");
const output = path.resolve(process.argv[2]);
const { chromium } = require(process.env.PREVIEW_TEST_PLAYWRIGHT || "playwright");
const extensionPath = process.env.PREVIEW_TEST_EXTENSION || path.join(root, "browser-extension");
const applicationPath = process.env.PREVIEW_TEST_APPLICATION || path.join(root, "bin/MemoryCompression.exe");
const manifest = JSON.parse(fs.readFileSync(path.join(extensionPath, "manifest.json"), "utf8"));
const extensionId = [...crypto.createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16)]
  .map(n => String.fromCharCode(97 + (n >> 4), 97 + (n & 15))).join("");
const bridgePort = 47832;
const bridgeUrl = `http://127.0.0.1:${bridgePort}/v1/tabs`;
const journal = path.join(output, "events");
const children = [];
const runId = crypto.randomUUID();
let sequence = 0;
let context;
fs.mkdirSync(output, { recursive: true });

function record(type, detail = {}) {
  fs.appendFileSync(path.join(output, "browser-ledger.jsonl"), JSON.stringify({
    schema_version: 1, sequence: ++sequence, event_id: crypto.randomUUID(),
    time_utc: new Date().toISOString(), run_id: runId, type, ...detail,
  }) + "\n");
  console.log(type);
}
async function until(check, message, timeout = 20000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    if (await check()) return;
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error(message);
}
async function freePort() {
  const server = net.createServer();
  await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));
  return port;
}
async function connectable(port) {
  return new Promise(resolve => {
    const socket = net.connect({ port, host: "127.0.0.1" });
    const finish = value => { socket.destroy(); resolve(value); };
    socket.once("connect", () => finish(true));
    socket.once("error", () => finish(false));
    socket.setTimeout(800, () => finish(false));
  });
}
function start(command, args, options = {}) {
  const child = spawn(command, args, { windowsHide: true, stdio: ["ignore", "pipe", "pipe"], ...options });
  children.push(child);
  const log = fs.createWriteStream(path.join(output, `child-${children.length}.log`), { flags: "a" });
  child.stdout.pipe(log, { end: false });
  child.stderr.pipe(log, { end: false });
  child.once("error", error => record("child_error", { message: error.message }));
  child.once("close", code => { log.end(); record("child_exit", { pid: child.pid, code }); });
  return child;
}
function events() {
  if (!fs.existsSync(journal)) return [];
  return fs.readdirSync(journal).filter(name => name.endsWith(".jsonl")).flatMap(name => {
    const lines = fs.readFileSync(path.join(journal, name), "utf8").split("\n");
    return lines.slice(0, -1).filter(Boolean).map(line => JSON.parse(line));
  });
}
function registered(server) {
  return events().find(event => event.type === "preview_registered" && event.candidate.port === server.port && event.candidate.processes[0].pid === server.child.pid);
}
async function plumbingServer(recognized) {
  // Tiny generated Node servers test lifecycle plumbing, not Next.js compatibility.
  const port = await freePort();
  const script = path.join(output, `server-${port}`, recognized ? "node_modules/next/dist/server/lib/start-server.js" : "plain-api.cjs");
  fs.mkdirSync(path.dirname(script), { recursive: true });
  fs.writeFileSync(script, 'require("node:http").createServer((q,s)=>{s.setHeader("Content-Type","text/html");s.end("<!doctype html><title>Preview cleanup test</title><h1>Test-owned preview</h1>");}).listen(Number(process.env.PREVIEW_TEST_SERVER_PORT),"127.0.0.1");');
  // Match the internal Next.js worker's no-argument entry shape. The test port
  // is configuration, not a fictitious framework subcommand.
  const args = [script];
  if (!recognized) args.push("--label=node_modules/next/dist/bin/next");
  const child = start(process.execPath, args, { env: { ...process.env, PREVIEW_TEST_SERVER_PORT: String(port) } });
  await until(() => connectable(port), "Test server failed to listen.");
  return { child, port, url: `http://127.0.0.1:${port}/` };
}

(async () => {
  assert.equal(await connectable(bridgePort), false, "The open-source bridge port is in use; no existing process was stopped.");
  // Run the actual GUI application, not host-only mode. No optimizer is configured.
  const host = start(applicationPath, [
    "--window-left", "-10000", "--window-top", "-10000",
    "--preview-log-directory", journal, "--interval", "5",
  ]);
  await until(() => connectable(bridgePort), "The engine-free GUI application did not open its bridge.");
  const executablePath = process.env.PREVIEW_TEST_CHROMIUM || chromium.executablePath();
  assert.ok(fs.existsSync(executablePath), "Install Chromium with npx playwright install chromium, or pass -ChromiumPath.");
  const profile = path.join(output, "isolated-profile");
  context = await chromium.launchPersistentContext(profile, {
    executablePath, headless: true, ignoreDefaultArgs: ["--disable-extensions"],
    args: [`--disable-extensions-except=${extensionPath}`, `--load-extension=${extensionPath}`, "--window-position=-10000,-10000"],
  });
  const windows = execFileSync("powershell.exe", ["-NoProfile", "-File", path.join(__dirname, "Assert-BackgroundWindows.ps1"), "-ProfileDirectory", profile], { windowsHide: true, encoding: "utf8" });
  fs.writeFileSync(path.join(output, "background-windows.json"), windows);
  const worker = context.serviceWorkers()[0] || await context.waitForEvent("serviceworker");
  assert.equal(new URL(worker.url()).hostname, extensionId);
  const popup = await context.newPage();
  await popup.goto(`chrome-extension://${extensionId}/popup.html`);
  await popup.locator("#status").filter({ hasText: "已连接" }).waitFor();
  await popup.screenshot({ path: path.join(output, "connected-popup.png") });
  await popup.getByRole("button", { name: "重新连接" }).focus();
  assert.equal(await popup.getByRole("button", { name: "重新连接" }).evaluate(button => button === document.activeElement), true);
  record("engine_free_gui_and_fresh_extension_connected", { extension_id: extensionId, host_pid: host.pid });

  const first = await plumbingServer(true);
  const second = await plumbingServer(true);
  const api = await plumbingServer(false);
  const a = await context.newPage();
  const b = await context.newPage();
  const c = await context.newPage();
  await a.goto(first.url);
  await b.goto(first.url);
  await c.goto(second.url);
  await until(() => registered(first) && registered(second), "The real extension did not register the test listeners.");
  record("two_ports_registered", { ports: [first.port, second.port], host_event_id: registered(first).event_id });

  // The browser supplies its real web Origin. Do not impersonate extension requests.
  const rejected = a.waitForResponse(response => response.url() === bridgeUrl && response.request().method() === "POST");
  await a.evaluate(async url => { await fetch(url, { method: "POST", mode: "no-cors", body: "{}" }); }, bridgeUrl);
  assert.equal((await rejected).status(), 403);
  record("ordinary_web_origin_rejected");

  await a.reload();
  await new Promise(resolve => setTimeout(resolve, 4200));
  assert.equal(first.child.exitCode, null);
  await a.close();
  await new Promise(resolve => setTimeout(resolve, 4200));
  assert.equal(first.child.exitCode, null);
  record("refresh_and_duplicate_tab_kept_service");
  await b.close();
  await until(() => first.child.exitCode !== null && events().some(e => e.type === "preview_stopped" && e.port === first.port), "Last-tab close failed to stop the matching process.");
  assert.equal(second.child.exitCode, null);
  assert.equal(api.child.exitCode, null);
  record("last_tab_stopped_only_matching_service", { port: first.port, pid: first.child.pid });
  const apiPage = await context.newPage();
  await apiPage.goto(api.url);
  await new Promise(resolve => setTimeout(resolve, 1500));
  await apiPage.close();
  await new Promise(resolve => setTimeout(resolve, 4200));
  assert.equal(api.child.exitCode, null);
  assert.equal(events().some(event => event.type === "preview_registered" && event.candidate.port === api.port), false);
  record("ordinary_node_api_with_framework_label_left_running");
  await c.close();
  await until(() => second.child.exitCode !== null, "Second preview failed to stop.");

  if (process.env.PREVIEW_TEST_NEXT_PROJECT) {
    const project = process.env.PREVIEW_TEST_NEXT_PROJECT;
    const cli = path.join(project, "node_modules/next/dist/bin/next");
    assert.ok(fs.existsSync(cli) && fs.existsSync(path.join(project, ".next/BUILD_ID")), "The optional real-Next test requires an installed, already-built project.");
    const port = await freePort();
    const child = start(process.execPath, [cli, "start", "-H", "127.0.0.1", "-p", String(port)], { cwd: project });
    const next = { child, port };
    await until(() => connectable(port), "The extra real Next.js instance failed to start.");
    const page = await context.newPage();
    await page.goto(`http://127.0.0.1:${port}/__preview_cleanup_test__`);
    await until(() => registered(next), "The real Next.js listener was not registered.");
    const registration = registered(next);
    await page.close();
    await until(() => child.exitCode !== null && events().some(e => e.type === "preview_stopped" && e.port === port), "The real Next.js process failed to stop.");
    assert.equal(await connectable(port), false);
    record("real_next_process_stopped_without_optimizer", { port, pid: child.pid, host_event_id: registration.event_id });
  }
  assert.equal(host.exitCode, null);
  record("PASS", { checks: "fresh profile, engine-free real GUI, matching listener exit, refresh, duplicate tabs, unrelated ports, ordinary API, real browser Origin, popup" });
})().catch(error => {
  record("FAIL", { message: error.stack });
  process.exitCode = 1;
}).finally(async () => {
  if (context) await context.close();
  for (const child of children) {
    if (child.exitCode === null && child.pid) {
      child.kill();
      await Promise.race([new Promise(resolve => child.once("close", resolve)), new Promise(resolve => setTimeout(resolve, 3000))]);
    }
  }
});
