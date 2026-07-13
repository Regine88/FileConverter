# Requires: Windows PowerShell 5+ or PowerShell 7+
# Validates that Application version, AssemblyVersion, WiX ProductVersion, Product.wxs, and version.xml agree.
# Usage: powershell -File scripts/Check-VersionConsistency.ps1 [-RequireManifestSecrets]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$RequireManifestSecrets
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

function Get-AppVersion {
    $path = Join-Path $RepoRoot "Application\FileConverter\Application.xaml.cs"
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch 'Major\s*=\s*(\d+)\s*,\s*\r?\n\s*Minor\s*=\s*(\d+)\s*,\s*\r?\n\s*Patch\s*=\s*(\d+)') {
        throw "Unable to parse ApplicationVersion from Application.xaml.cs"
    }
    return [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Semantic = if ([int]$Matches[3] -eq 0) { "$($Matches[1]).$($Matches[2])" } else { "$($Matches[1]).$($Matches[2]).$($Matches[3])" }
        Full = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }
}

function Get-AssemblyVersion {
    $path = Join-Path $RepoRoot "Application\FileConverter\Properties\AssemblyInfo.cs"
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)\.(\d+)"\)') {
        throw "Unable to parse AssemblyVersion"
    }
    return "$($Matches[1]).$($Matches[2]).$($Matches[3])"
}

function Get-WixProductVersion {
    $path = Join-Path $RepoRoot "Installer\Installer.wixproj"
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch '<ProductVersion>([^<]+)</ProductVersion>') {
        throw "Unable to parse ProductVersion from Installer.wixproj"
    }
    return $Matches[1].Trim()
}

function Get-ProductWxsVersion {
    $path = Join-Path $RepoRoot "Installer\Product.wxs"
    $text = Get-Content -Raw -Path $path
    if ($text -notmatch 'Version="([^"]+)"') {
        throw "Unable to parse Package Version from Product.wxs"
    }
    return $Matches[1].Trim()
}

$app = Get-AppVersion
$asm = Get-AssemblyVersion
$wix = Get-WixProductVersion
$product = Get-ProductWxsVersion

Write-Host "Application.xaml.cs : $($app.Full) (display $($app.Semantic))"
Write-Host "AssemblyVersion     : $asm"
Write-Host "Installer.wixproj   : $wix"
Write-Host "Product.wxs         : $product"

$expectedFull = $app.Full
if ($asm -ne $expectedFull) { throw "AssemblyVersion $asm != app $expectedFull" }
if ($wix -ne $expectedFull) { throw "Installer ProductVersion $wix != app $expectedFull" }
if ($product -ne $expectedFull) { throw "Product.wxs Version $product != app $expectedFull" }

$manifestPath = Join-Path $RepoRoot "version.xml"
if (-not (Test-Path $manifestPath)) {
    throw "version.xml missing"
}

[xml]$manifest = Get-Content -Path $manifestPath
$latest = $manifest.Version.Latest
$manFull = "$($latest.Major).$($latest.Minor).$($latest.Patch)"
if ([string]::IsNullOrEmpty($latest.Patch)) { $manFull = "$($latest.Major).$($latest.Minor).0" }

Write-Host "version.xml Latest  : $manFull"
if ($manFull -ne $expectedFull) {
    throw "version.xml Latest $manFull != app $expectedFull"
}

$sha = [string]$manifest.Version.SHA256
$publisher = [string]$manifest.Version.Publisher

if ($RequireManifestSecrets) {
    if ([string]::IsNullOrWhiteSpace($sha) -or $sha.Trim().Length -ne 64) {
        throw "version.xml SHA256 must be a 64-char hex digest for release manifests."
    }
    if ($sha -match '^0+$') {
        throw "version.xml SHA256 is a zero placeholder; replace with the real installer digest before release."
    }
    if ([string]::IsNullOrWhiteSpace($publisher) -or $publisher -match 'Placeholder') {
        throw "version.xml Publisher must be set to the real Authenticode subject before release."
    }
} else {
    Write-Host "Manifest secret fields present: SHA256=$([bool]$sha) Publisher=$([bool]$publisher) (strict check disabled)"
}

# x86 is not a supported release channel.
$x86 = Join-Path $RepoRoot "version (x86).xml"
if (Test-Path $x86) {
    throw "version (x86).xml is present but x86 is unsupported. Remove it or restore formal x86 support."
}

Write-Host "Version consistency OK."
