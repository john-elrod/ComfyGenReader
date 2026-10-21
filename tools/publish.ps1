# Builds the portable zip: artifacts\ComfyGenReader-win-x64.zip (Windows PowerShell 5.1 compatible).
# -Version 1.2.3 stamps the assembly version and names the zip ComfyGenReader-1.2.3-win-x64.zip.
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root 'artifacts\ComfyGenReader'
$zipName = if ($Version) { "ComfyGenReader-$Version-win-x64.zip" } else { 'ComfyGenReader-win-x64.zip' }
$zip  = Join-Path $root "artifacts\$zipName"

# Idempotent: re-downloads only when third_party\ffmpeg does not match the pinned sha256.
& (Join-Path $PSScriptRoot 'get-ffmpeg.ps1')

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }

$publishArgs = @((Join-Path $root 'src\ComfyGenReader'), '-c', 'Release', '-o', $out)
if ($Version) { $publishArgs += "-p:Version=$Version" }
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Copy-Item -Path (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $out
Get-ChildItem -Path $out -Recurse -Filter *.pdb | Remove-Item -Force

# Compress-Archive in 5.1 writes backslash entry names; build the zip with .NET using '/' separators.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [IO.File]::Open($zip, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = (Split-Path -Parent $out).TrimEnd('\') + '\'
        foreach ($f in Get-ChildItem -Path $out -Recurse -File) {
            $name = $f.FullName.Substring($base.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
} finally { $fs.Dispose() }

Write-Host ("Created {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
