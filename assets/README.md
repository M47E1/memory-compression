# Project artwork

Small, reusable artwork for Memory Compression. These files contain no local
paths, browser profiles, runtime records, or test logs.

| File | Size | Intended use |
| --- | --- | --- |
| `cover-portfolio.webp` | 640 × 640 | Square portfolio cards; use `object-fit: contain`, centered. |
| `cover-github.png` | 1280 × 640 | README header and repository social preview; keep the whole image visible. |
| `app-window.png` | 486 × 257 | Actual Windows application interface with no external engine configured. |

The two covers were generated with the built-in ImageGen tool, then exported at
web-friendly sizes with metadata removed. They are conceptual project artwork,
not screenshots of the application. The application image shows the real
WinForms interface and has been checked for personal information.

The visual describes a closed localhost preview and its matching stopped server.
It does not depict a memory-compression algorithm, measured memory savings, or a
system-performance benchmark. The artwork is distributed under the repository's
[MIT License](../LICENSE).

The README uses the banner directly. The repository's separate social-preview
setting requires uploading `cover-github.png` in **Settings → General → Social
preview**; committing a README image alone does not set that field. The banner
matches [GitHub's recommended 1280 × 640 size](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview).
