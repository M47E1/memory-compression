# Changelog

## 1.3.0 — 2026-10-18

- Memory cleanup is manual-only. Opening the app, logging in, or waiting for a timer never starts a memory engine.
- The optional engine is Windows Memory Cleaner 3.0.8, with `/StandbyListLowPriority` as the only permitted mode. Process working-set cleanup, full standby purges, and forced modified-page writes are not enabled.
- The optional installer downloads the official executable and verifies its release digest and Authenticode signature. No third-party executable is included in this project's releases.
- Engine elevation uses a normal Windows UAC confirmation for each manual run; the preview tool does not need administrator privileges. Missing engines, cancelled elevation, and failed runs do not pause preview cleanup.
- The existing preview retry interval remains independent of memory cleanup. The conservative Node classifier, extension identity, loopback bridge, and append-only preview journal are unchanged.
- A completed message requires a successful engine exit and a corresponding successful engine record; available-memory fluctuations are no longer presented as memory freed by this tool.

This release does not promise faster application switching or fixed memory savings. Cache cleanup is not a substitute for finding a memory-heavy or unresponsive application.

## 1.0.0

Initial public release with an optional external engine and localhost preview lifecycle cleanup.
