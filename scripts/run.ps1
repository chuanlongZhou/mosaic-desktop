param([switch]$Dashboard, [switch]$Palette, [switch]$Library, [switch]$Workspaces, [switch]$Pet, [switch]$Exit, [string]$DataDirectory)
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-MosaicDotnet -Arguments @('build', (Join-Path $mosaicRoot 'src\Mosaic.Host\Mosaic.Host.csproj'), '-c', 'Release', '--nologo')
$mosaicExecutable = Join-Path $mosaicRoot 'src\Mosaic.Host\bin\Release\net10.0-windows\Mosaic.exe'
$mosaicArguments = [System.Collections.Generic.List[string]]::new()
if ($Dashboard) { $mosaicArguments.Add('--dashboard') }
if ($Palette) { $mosaicArguments.Add('--palette') }
if ($Library) { $mosaicArguments.Add('--library') }
if ($Workspaces) { $mosaicArguments.Add('--workspaces') }
if ($Pet) { $mosaicArguments.Add('--pet') }
if ($Exit) { $mosaicArguments.Add('--exit') }
if ($DataDirectory) { $mosaicArguments.Add('--data-dir'); $mosaicArguments.Add('"' + [IO.Path]::GetFullPath($DataDirectory) + '"') }
$mosaicWindowStyle = if ($Exit) { 'Hidden' } else { 'Normal' }
$mosaicStart = @{ FilePath=$mosaicExecutable; WorkingDirectory=$mosaicRoot; WindowStyle=$mosaicWindowStyle }
if ($mosaicArguments.Count) { $mosaicStart.ArgumentList = $mosaicArguments.ToArray() }
Start-Process @mosaicStart
