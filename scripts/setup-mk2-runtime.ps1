$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$native = Join-Path $root 'src/Mosaic.Host/Native/FFmpeg'
$manifest = Get-Content -LiteralPath (Join-Path $native 'runtime.json') -Raw | ConvertFrom-Json
$ready = $true
foreach ($file in $manifest.files) {
    $path = Join-Path $native $file.file
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $file.sha256) { $ready = $false; break }
}
if ($ready) { return }
$cache = Join-Path $root '.tools/mk2-ffmpeg'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$archive = Join-Path $cache 'runtime.zip'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $manifest.archive_sha256) {
    Invoke-WebRequest -Uri $manifest.archive_url -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $manifest.archive_sha256) { throw 'mk2 FFmpeg 运行库下载校验失败。' }
$expanded = Join-Path $cache 'expanded'
Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
foreach ($file in $manifest.files) {
    $candidate = Get-ChildItem -LiteralPath $expanded -Recurse -File -Filter $file.file | Select-Object -First 1
    if (!$candidate -or (Get-FileHash -LiteralPath $candidate.FullName).Hash.ToLowerInvariant() -ne $file.sha256) { throw "mk2 运行库校验失败：$($file.file)" }
    Copy-Item -LiteralPath $candidate.FullName -Destination (Join-Path $native $file.file) -Force
}
Write-Output 'mk2 视频运行库已准备并校验。'
