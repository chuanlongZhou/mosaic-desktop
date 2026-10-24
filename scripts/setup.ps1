$ErrorActionPreference = 'Stop'
$mosaicRoot = Split-Path -Parent $PSScriptRoot
$mosaicToolDirectory = Join-Path $mosaicRoot '.tools'
New-Item -ItemType Directory -Force -Path $mosaicToolDirectory | Out-Null
$mosaicInstaller = Join-Path $mosaicToolDirectory 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $mosaicInstaller
& $mosaicInstaller -Channel 10.0 -Quality GA -Architecture x64 -InstallDir (Join-Path $mosaicToolDirectory 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw '.NET SDK 安装失败。' }
Write-Output '项目独立 SDK 已就绪；运行 scripts\run.ps1 启动。'
