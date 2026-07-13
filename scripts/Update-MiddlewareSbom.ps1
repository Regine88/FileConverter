# Regenerates docs/MIDDLEWARE_SBOM.md with current SHA-256 hashes.
$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

$files = @(
  "Middleware\ffmpeg\ffmpeg.exe",
  "Middleware\gs\gswin64c.exe",
  "Middleware\gs\gsdll64.dll",
  "Middleware\Markdown.Xaml.dll",
  "Middleware\Ripper.dll",
  "Middleware\yeti.mmedia.dll"
)

$lines = @(
  "# Middleware binary inventory (verifiable)",
  "",
  "Generated: $(Get-Date -Format 'yyyy-MM-dd')",
  "",
  "| Path | Size (bytes) | SHA-256 |",
  "|---|---:|---|"
)

foreach ($f in $files) {
  if (Test-Path $f) {
    $h = (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant()
    $len = (Get-Item $f).Length
    $lines += "| ``$f`` | $len | ``$h`` |"
  } else {
    $lines += "| ``$f`` | MISSING |  |"
  }
}

$lines += ""
$lines += "## Update process"
$lines += "1. Replace the binary and re-run: ``pwsh -File scripts/Update-MiddlewareSbom.ps1``."
$lines += "2. Commit the new hashes with the binary change."
$lines += "3. Optional CI step: fail if on-disk hashes drift from this document."
$lines += ""
$lines += "## Licenses"
$lines += "- FFmpeg: see ``Middleware/ffmpeg/LICENSE``"
$lines += "- Ghostscript: vendor license; keep gswin64c.exe and gsdll64.dll in lockstep"

$out = Join-Path $RepoRoot "docs\MIDDLEWARE_SBOM.md"
$lines -join "`n" | Set-Content -Path $out -Encoding UTF8
Write-Host "Wrote $out"
