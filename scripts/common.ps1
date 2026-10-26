$ErrorActionPreference = 'Stop'
$mosaicRoot = Split-Path -Parent $PSScriptRoot
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $mosaicRoot '.tools\dotnet-home' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $mosaicRoot '.tools\nuget-packages' }
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$mosaicLocalDotnet = Join-Path $mosaicRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $mosaicLocalDotnet) {
    $mosaicDotnet = $mosaicLocalDotnet
} else {
    $mosaicInstalledDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $mosaicInstalledDotnet) { throw '请先安装 .NET 10 SDK，或运行 scripts\setup.ps1。' }
    $mosaicDotnet = $mosaicInstalledDotnet.Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
function Invoke-MosaicDotnet {
    param([string[]]$Arguments)
    Push-Location -LiteralPath $mosaicRoot
    try {
        & $mosaicDotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet 执行失败，退出码 $LASTEXITCODE。" }
    } finally { Pop-Location }
}
