. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-MosaicDotnet -Arguments @('build', (Join-Path $mosaicRoot 'Mosaic.Desktop.sln'), '-c', 'Release', '--nologo')
