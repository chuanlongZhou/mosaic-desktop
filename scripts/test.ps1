. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-MosaicDotnet -Arguments @('run', '--project', (Join-Path $mosaicRoot 'tests\Mosaic.Tests\Mosaic.Tests.csproj'), '-c', 'Release')
