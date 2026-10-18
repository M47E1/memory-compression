# Adversarial review

This document is a curated review summary, not a runtime log. It contains no local
user identifiers, browsing records, real process IDs, or machine-specific paths.

## 1.3.0 publication checks

Memory cleanup is now manual-only, with the official Windows Memory Cleaner
3.0.8 executable and `/StandbyListLowPriority` as the only supported engine/mode.
The optional installer checks both the release digest and Authenticode signature;
the app checks the expected bytes again before a manual UAC launch. No third-party
binary is shipped. Preview classification, protocol identities, journal format,
and earlier-denial behavior are unchanged.

Fresh local checks passed 8 JavaScript tests, 91 controller assertions, 50 Node
classification cases, 28 engine-policy/manual-only assertions, and 26 actual
off-screen WinForms runtime assertions. These cover startup/timer non-execution,
manual results, refused files, and preview independence while a simulated engine
is busy, fails, or reports cancelled authorization. Runtime test doubles do not
execute a real memory cleaner or require UAC. They are not performance benchmarks.

The browser and real-framework results below describe the original 1.0.0 review,
not a new 1.3.0 browser run. Version 1.3.0 publication did not start an extra browser
under local memory pressure. No second physical Windows machine or enterprise
policy configuration was tested.

## 1.0.0 fixed blocker: process misclassification

**Severity: high. Status: repaired with regression coverage.**

The original classifier in `src/PreviewCleanup.cs`, method `IsPreview`, searched
the entire Node command line for framework-looking text. A plain API with an
unrelated argument such as `--label=node_modules/next/dist/bin/next`, or an inline
script containing the same text, is classified as a preview. The parent-shell
heuristic has the same problem with shell commands that merely mention `next dev`.

The original recognized CLI paths were not limited to serving modes. A Next.js, Vite, or
Astro build process could also be classified as a preview if it exposed a listening
port, for example through a debugger. This contradicts the requirement to leave
ordinary APIs and unrelated background work alone.

The same classifier also missed a common normalized entry path containing
`node_modules/.bin/../next/`. This is a false negative rather than an unsafe stop.

`tests/AdversarialTests.cs` exercises the actual classifier against in-memory
Windows process facts. The first run found 17 failing cases out of 35. The test
does not start, modify, or terminate any operating-system process.

The repair parses Windows arguments, inspects the actual Node entry script,
normalizes path segments without applying this tool's working directory to
relative scripts, and checks exact framework entry paths and serving modes.
Inline/eval/print scripts, unknown Node options, builds, and arbitrary shell
launchers are not managed. An argument that merely names a framework cannot
register a service. The existing PID, creation-time, port-owner, and
permission-denial checks are unchanged.

The expanded regression run passes 50 classification cases. The controller
passes 91 assertions, and the actual WinForms runtime passes 27 assertions
covering absent, invalid, failing, successful, and busy optional engines. Six
JavaScript tests cover extension eligibility, permissions, protocol identity,
defaults, and the packaging file list. These counts describe local checks, not
coverage of every possible command line or deployment.

The browser run used the actual engine-free GUI and the bundled extension in a
fresh isolated Chromium profile. Closing the last page stopped the matching
listener; refresh and a second tab kept it running. Other ports and a plain Node
API carrying a framework-looking label remained alive, and the API was never
registered. A normal web page's request to the bridge received HTTP 403. An
additional instance of an already-built, real Next.js application was also
registered and stopped on last-page close, with its process exit and released
port checked. Generated micro-servers exercise lifecycle plumbing only; they
are not substitutes for real Vite or Astro compatibility tests.

The parser follows the documented Windows argument API and a conservative
subset of Node options. Serving modes follow the framework CLI contracts:
[Windows argument parsing](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-commandlinetoargvw),
[Node CLI](https://nodejs.org/api/cli.html),
[Next.js CLI](https://nextjs.org/docs/app/api-reference/cli/next), and
[Vite CLI](https://vite.dev/guide/cli).

## Other reviewed boundaries

- The bridge listens only on IPv4 loopback and limits browser requests to the
  bundled extension origin; ordinary web pages are not authorized.
- Process identity is rechecked before termination. A prior permission or identity
  denial is not relaxed by the periodic retry path.
- Optional-engine errors do not pause preview cleanup. No third-party memory
  optimizer binary is included or required.
- Publication must use an explicit file list. Local event journals, browser
  profiles, test output, dependency directories, old binaries, and personal
  backups are not release content.

## Limits

Origin checks are a browser boundary, not authentication against arbitrary code
already executing as the same local Windows user. Run without administrator
privileges. Process termination is not application-level graceful shutdown.

This review is not a claim of zero vulnerabilities or independent third-party
certification. The program name does not imply that the project implements the
Windows memory-compression algorithm. No second physical Windows machine, all
framework versions, or enterprise security policies have been validated.
