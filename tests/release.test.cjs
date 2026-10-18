"use strict";

const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");

test("extension, host, and port filtering use the same public protocol identity", () => {
  const host = read("src/PreviewCleanup.cs");
  const id = host.match(/ExtensionId = "([a-p]{32})"/)[1];
  const port = Number(host.match(/DefaultPort = (\d+)/)[1]);
  const manifest = JSON.parse(read("browser-extension/manifest.json"));
  const crypto = require("node:crypto");
  const derived = [...crypto.createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16)]
    .map(n => String.fromCharCode(97 + (n >> 4), 97 + (n & 15))).join("");
  assert.equal(id, derived);
  assert.match(read("browser-extension/background.js"), new RegExp(`127\\.0\\.0\\.1:${port}/v1/tabs`));
  assert.match(read("browser-extension/ports.js"), new RegExp(`port !== ${port}`));
  assert.equal(port, 47832);
});

test("external engine is opt-in and no profile path is embedded in application defaults", () => {
  const app = read("src/MemoryCompression.cs");
  assert.match(app, /OptimizerPath = string\.Empty/);
  assert.match(app, /OptimizerArguments = string\.Empty/);
  assert.match(app, /SpecialFolder\.LocalApplicationData/);
  assert.doesNotMatch(app, /[A-Z]:\\Users\\/i);
  assert.match(app, /MemoryCompression-v1/);
  assert.match(read("LICENSE"), /MIT License/);
});

test("package file list excludes local history, old binaries, and test results", () => {
  const packaging = read("Package.ps1");
  const match = packaging.match(/\$sourceFiles = @\(([\s\S]*?)\n\)/);
  assert.ok(match, "Explicit source file list is required.");
  const files = [...match[1].matchAll(/'([^']+)'/g)].map(m => m[1]);
  assert.ok(files.length > 12);
  for (const file of files) {
    assert.ok(fs.existsSync(path.join(root, file)), file);
    assert.doesNotMatch(file, /test-output|archive|node_modules|\.exe$|\.jsonl$/i);
  }
});
