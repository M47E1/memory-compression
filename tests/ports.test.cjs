"use strict";

const { test } = require("node:test");
const assert = require("node:assert/strict");
const { localhostPort, openPorts } = require("../browser-extension/ports.js");

test("only exact loopback web origins are eligible", () => {
  assert.equal(localhostPort("http://localhost:3001/preview"), 3001);
  assert.equal(localhostPort("http://127.0.0.1:5191/"), 5191);
  assert.equal(localhostPort("https://[::1]:3443/"), 3443);
  for (const url of ["https://example.com", "http://localhost.example.com:3001", "http://localhost@evil.example:3001", "file:///C:/localhost/", "not a url", "http://user:pass@localhost:3001"]) {
    assert.equal(localhostPort(url), null, url);
  }
});

test("duplicates and background or discarded tabs still keep the preview open", () => {
  assert.deepEqual(openPorts([
    { url: "http://localhost:3001/", active: false },
    { url: "http://127.0.0.1:3001/", discarded: true },
    { url: "http://localhost:5191/", frozen: true },
    { url: "https://example.com/", pendingUrl: "http://localhost:5147/" },
    { url: "http://127.0.0.1:47832/" },
    {},
  ]), [3001, 5147, 5191]);
});

test("no browser-wide tab permission, no content injection, no remote host access", () => {
  const manifest = require("../browser-extension/manifest.json");
  assert.deepEqual(manifest.permissions, ["storage", "alarms"]);
  assert.equal(manifest.content_scripts, undefined);
  for (const pattern of manifest.host_permissions) {
    const host = new URL(pattern.replace("/*", "/")).hostname;
    assert.ok(["localhost", "127.0.0.1", "[::1]"].includes(host));
  }
  const crypto = require("node:crypto");
  const id = [...crypto.createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16)]
    .map((value) => String.fromCharCode(97 + (value >> 4), 97 + (value & 15))).join("");
  assert.equal(id, "dgpfiadablpfojbalengndogkiidplcb");
});
