. (Join-Path $PSScriptRoot 'common.ps1')
$mosaicOutput = Join-Path $mosaicRoot 'artifacts\Mosaic-v0.7.0-win-x64'
Invoke-MosaicDotnet -Arguments @('publish', (Join-Path $mosaicRoot 'src\Mosaic.Host\Mosaic.Host.csproj'), '-c', 'Release', '--self-contained', 'false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $mosaicOutput)
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'packaging\README.md') -Destination (Join-Path $mosaicOutput 'README.md')
Compress-Archive -LiteralPath $mosaicOutput -DestinationPath ($mosaicOutput + '.zip') -Force
Write-Output "可运行目录：$mosaicOutput"
Write-Output "压缩包：$mosaicOutput.zip"
