# Downloads the pinned FFmpeg build (tools\ffmpeg.lock.json) into third_party\ffmpeg\.
# Works in Windows PowerShell 5.1. Idempotent.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root   = Split-Path -Parent $PSScriptRoot
$lock   = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'ffmpeg.lock.json') | ConvertFrom-Json
$target = Join-Path $root 'third_party\ffmpeg'
$marker = Join-Path $target '.lock-sha256'
$sha    = $lock.sha256.ToLowerInvariant()

if ((Test-Path (Join-Path $target 'ffprobe.exe')) -and (Test-Path $marker) -and
    ((Get-Content -Raw -Path $marker).Trim() -eq $sha)) {
    Write-Host "FFmpeg already present in $target"
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('ffmpeg-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $zip = Join-Path $tmp 'ffmpeg.zip'
    Write-Host "Downloading $($lock.url)"
    Invoke-WebRequest -Uri $lock.url -OutFile $zip -UseBasicParsing

    $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha) { throw "SHA256 mismatch: expected $sha but got $actual" }

    $extract = Join-Path $tmp 'x'
    Expand-Archive -Path $zip -DestinationPath $extract
    $bin = Get-ChildItem -Path $extract -Directory | ForEach-Object { Join-Path $_.FullName 'bin' } |
           Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $bin) { throw 'bin folder not found in archive' }

    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    New-Item -ItemType Directory -Path $target | Out-Null
    Copy-Item -Path (Join-Path $bin '*') -Destination $target -Recurse
    Set-Content -Path $marker -Value $sha -Encoding ASCII
    Write-Host "FFmpeg installed to $target"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
