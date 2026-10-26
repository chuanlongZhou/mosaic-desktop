. (Join-Path $PSScriptRoot 'common.ps1')
[xml]$mosaicProject = Get-Content -LiteralPath (Join-Path $mosaicRoot 'src\Mosaic.Host\Mosaic.Host.csproj')
$mosaicVersion = [string]$mosaicProject.Project.PropertyGroup.Version
if ($mosaicVersion -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本号无效。' }
$mosaicOutput = Join-Path $mosaicRoot "artifacts\Mosaic-v$mosaicVersion-win-x64"
Invoke-MosaicDotnet -Arguments @('publish', (Join-Path $mosaicRoot 'src\Mosaic.Host\Mosaic.Host.csproj'), '-c', 'Release', '--self-contained', 'false', '-m:1', '--artifacts-path', (Join-Path $mosaicRoot '.artifacts\publish-build'), '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $mosaicOutput)
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'packaging\README.md') -Destination (Join-Path $mosaicOutput 'README.md')
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'docs\PET_EXPRESSIONS.md') -Destination (Join-Path $mosaicOutput 'PET_EXPRESSIONS.md')
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'docs\AI_PET_PERSONALITIES.md') -Destination (Join-Path $mosaicOutput 'AI_PET_PERSONALITIES.md')
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'docs\PET_CHAT_LINES.md') -Destination (Join-Path $mosaicOutput 'PET_CHAT_LINES.md')
Copy-Item -LiteralPath (Join-Path $mosaicRoot 'docs\NORMAL_PET_VARIANTS.md') -Destination (Join-Path $mosaicOutput 'NORMAL_PET_VARIANTS.md')
$mosaicExpressionAssets = Join-Path $mosaicOutput 'assets'
New-Item -ItemType Directory -Path $mosaicExpressionAssets -Force | Out-Null
foreach ($mosaicExpressionImage in @('pet-expressions-original.png', 'pet-expressions-deepseek-chibi.png', 'pet-expressions-deepseek-standard.png', 'pet-keyframes-original.png', 'pet-keyframes-deepseek-chibi.png', 'pet-keyframes-deepseek-standard.png', 'ai-pet-characters.png', 'pet-expressions-gpt.png', 'pet-expressions-gemini.png', 'pet-expressions-claude.png', 'pet-expressions-qwen.png', 'pet-keyframes-gpt.png', 'pet-keyframes-gemini.png', 'pet-keyframes-claude.png', 'pet-keyframes-qwen.png', 'ai-pet-standard-characters.png', 'pet-expressions-gpt-standard.png', 'pet-expressions-gemini-standard.png', 'pet-expressions-claude-standard.png', 'pet-expressions-qwen-standard.png', 'pet-keyframes-gpt-standard.png', 'pet-keyframes-gemini-standard.png', 'pet-keyframes-claude-standard.png', 'pet-keyframes-qwen-standard.png')) {
    Copy-Item -LiteralPath (Join-Path $mosaicRoot "docs\assets\$mosaicExpressionImage") -Destination (Join-Path $mosaicExpressionAssets $mosaicExpressionImage)
}
Compress-Archive -LiteralPath $mosaicOutput -DestinationPath ($mosaicOutput + '.zip') -Force
# Remove only versioned release outputs, after the new directory and ZIP exist.
$mosaicReleaseRoot = [IO.Path]::GetFullPath((Join-Path $mosaicRoot 'artifacts'))
$mosaicKeep = @([IO.Path]::GetFileName($mosaicOutput), ([IO.Path]::GetFileName($mosaicOutput) + '.zip'))
$mosaicInUse = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($mosaicProcess in Get-Process -Name Mosaic -ErrorAction SilentlyContinue) {
    $mosaicProcessPath = $mosaicProcess.Path
    if (-not $mosaicProcessPath) { continue }
    $mosaicRunningDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($mosaicProcessPath))
    $mosaicRunningName = [IO.Path]::GetFileName($mosaicRunningDirectory)
    if ([IO.Path]::GetDirectoryName($mosaicRunningDirectory) -eq $mosaicReleaseRoot -and $mosaicRunningName -match '^Mosaic-v\d+\.\d+(?:\.\d+)?-win-x64$') {
        [void]$mosaicInUse.Add($mosaicRunningName)
        [void]$mosaicInUse.Add($mosaicRunningName + '.zip')
    }
}
foreach ($mosaicOldRelease in Get-ChildItem -LiteralPath $mosaicReleaseRoot) {
    if ($mosaicOldRelease.Name -notmatch '^Mosaic-v\d+\.\d+(?:\.\d+)?-win-x64(?:\.zip)?$' -or $mosaicOldRelease.Name -in $mosaicKeep) { continue }
    if ($mosaicInUse.Contains($mosaicOldRelease.Name)) {
        Write-Output "保留正在使用的旧版发行包：$($mosaicOldRelease.Name)；退出后下次发布时清理。"
        continue
    }
    $mosaicDeletePath = [IO.Path]::GetFullPath($mosaicOldRelease.FullName)
    if ([IO.Path]::GetDirectoryName($mosaicDeletePath) -ne $mosaicReleaseRoot -or ($mosaicOldRelease.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "清理路径不安全：$mosaicDeletePath" }
    Remove-Item -LiteralPath $mosaicDeletePath -Recurse -Force -ErrorAction Stop
}
Write-Output "可运行目录：$mosaicOutput"
Write-Output "压缩包：$mosaicOutput.zip"
