# Middleware binary inventory (verifiable)

Generated: 2026-07-13

| Path | Size (bytes) | SHA-256 |
|---|---:|---|
| `Middleware\ffmpeg\ffmpeg.exe` | 99264000 | `5af82a0d4fe2b9eae211b967332ea97edfc51c6b328ca35b827e73eac560dc0d` |
| `Middleware\gs\gswin64c.exe` | 93696 | `0772e280480805c8d8277db2ff2ac56eca17c733835ccf1ab3a31150e75853b7` |
| `Middleware\gs\gsdll64.dll` | 24608256 | `f96834ba3dc32f81b6a70b11d894f3e495866e386f0e575be6f2acff0f0493b5` |
| `Middleware\Markdown.Xaml.dll` | 28160 | `7763da37fca67bce817f9a5ee01bbd2baf82b1b1be718e30e8cefc16662d3232` |
| `Middleware\Ripper.dll` | 28672 | `228737ea045f98cf6f400e06e932d17f3a9e15e1c738f1d8a083a469daf76bd1` |
| `Middleware\yeti.mmedia.dll` | 45056 | `066d8ec5867f30d7c9e36aee58b3cfac89610ea072bac6e26684915f9d4380b4` |

## Update process
1. Replace the binary and re-run: `pwsh -File scripts/Update-MiddlewareSbom.ps1` (or regenerate this table).
2. Commit the new hashes with the binary change.
3. Optional CI step: fail if on-disk hashes drift from this document.

## Licenses
- FFmpeg: see `Middleware/ffmpeg/LICENSE`
- Ghostscript: vendor license; keep gswin64c.exe and gsdll64.dll in lockstep
