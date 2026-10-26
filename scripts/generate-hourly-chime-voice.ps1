#Requires -Version 7.6
param(
    [string]$Python,
    [string]$RuntimeRepo,
    [string]$ModelPackage,
    [string]$ServiceSource
)
. (Join-Path $PSScriptRoot 'common.ps1')
$mosaicRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $mosaicRoot
if (!$Python) { $Python = Join-Path $workspaceRoot 'work/feiyu-runtime/venv/Scripts/python.exe' }
if (!$RuntimeRepo) { $RuntimeRepo = Join-Path $workspaceRoot 'work/feiyu-runtime/source' }
if (!$ModelPackage) { $ModelPackage = Join-Path $workspaceRoot 'work/feiyu-runtime/model' }
if (!$ServiceSource) { $ServiceSource = Join-Path $workspaceRoot 'work/astrbot_plugin_feiyu_voice/server' }
$destination = Join-Path $mosaicRoot 'src/Mosaic.Host/Assets/Voice/DeepSeekChibi'
$stage = Join-Path $mosaicRoot ('.artifacts/feiyu-voice/' + [Guid]::NewGuid().ToString('N'))
if (!(Test-Path -LiteralPath $Python)) { throw '请先准备肥鱼 CosyVoice3 CUDA 推理环境，或通过 -Python 指定环境。正常构建无需重新生成配音。' }
Invoke-MosaicDotnet -Arguments @('build', (Join-Path $mosaicRoot 'src/Mosaic.Core/Mosaic.Core.csproj'), '-c', 'Release', '-m:1', '-nr:false')
Add-Type -Path (Join-Path $mosaicRoot 'src/Mosaic.Core/bin/Release/net10.0/Mosaic.Core.dll')
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$catalog = foreach ($hour in 0..23) { [ordered]@{ hour = $hour; text = [Mosaic.Core.PetHourlyChime]::GetLine($hour) } }
$catalogPath = Join-Path $stage 'catalog.json'
$catalog | ConvertTo-Json | Set-Content -LiteralPath $catalogPath -Encoding utf8
$clips = Join-Path $stage 'clips'
& $Python -u (Join-Path $PSScriptRoot 'generate-feiyu-hourly-chimes.py') --runtime-repo $RuntimeRepo --service-source $ServiceSource --model-package $ModelPackage --catalog $catalogPath --output $clips
if ($LASTEXITCODE -ne 0) { throw '肥鱼语音合成失败，现有配音未替换。' }
$result = Get-Content -LiteralPath (Join-Path $clips 'generation.json') -Raw | ConvertFrom-Json
if (!$result.passed -or $result.clips -ne 24) { throw '未生成完整的 24 条报时语音，现有配音未替换。' }
Get-ChildItem -LiteralPath $clips -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }
Write-Output '已生成并替换 24 条肥鱼可爱声线报时配音。'
