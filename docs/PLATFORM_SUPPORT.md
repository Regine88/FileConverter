# Platform support

## Supported

| Platform | Status |
|---|---|
| Windows x64 | **Supported** — primary build (`Debug\|x64`, `Release\|x64`), installer, Shell extension, middleware (FFmpeg/Ghostscript x64) |

## Not supported

| Platform | Status |
|---|---|
| Windows x86 (32-bit) | **Unsupported** as of the 2026-07 reliability work. Solution configurations for x86 were removed to avoid a pseudo-supported release channel (WiX/language harvest path mismatches and x64-only middleware). |
| Windows ARM64 | Not currently built or tested. |

Project-level `x86`/`BUILD32` property groups may still exist in legacy `.csproj` files for historical reference; they are not part of the solution matrix and must not be used for releases without a full architecture pass (native middleware, SharpShell, WiX harvest paths, and version manifests).
