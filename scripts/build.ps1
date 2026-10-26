. (Join-Path $PSScriptRoot 'common.ps1')
& (Join-Path $PSScriptRoot 'setup-mk2-runtime.ps1')
Invoke-MosaicDotnet -Arguments @('build', (Join-Path $mosaicRoot 'Mosaic.Desktop.sln'), '-c', 'Release', '--nologo')
