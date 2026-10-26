param([switch]$NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
& (Join-Path $PSScriptRoot 'setup-mk2-runtime.ps1')
$demoProject = Join-Path $mosaicRoot 'src\Mosaic.PetWorldDemo\Mosaic.PetWorldDemo.csproj'
if (-not $NoBuild) { Invoke-MosaicDotnet -Arguments @('build', $demoProject, '-c', 'Release', '--nologo', '-m:1', '-nr:false') }
$demoExecutable = Join-Path $mosaicRoot 'src\Mosaic.PetWorldDemo\bin\Release\net10.0-windows\Mosaic.PetWorldDemo.exe'
if (-not (Test-Path -LiteralPath $demoExecutable)) { throw '请先不带 -NoBuild 运行一次以构建演示。' }
Start-Process -FilePath $demoExecutable -WorkingDirectory $mosaicRoot -WindowStyle Normal
