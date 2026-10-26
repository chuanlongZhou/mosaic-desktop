. (Join-Path $PSScriptRoot 'common.ps1')
& (Join-Path $PSScriptRoot 'setup-mk2-runtime.ps1')
Invoke-MosaicDotnet -Arguments @('run', '--project', (Join-Path $mosaicRoot 'tests\Mosaic.Tests\Mosaic.Tests.csproj'), '-c', 'Release')
