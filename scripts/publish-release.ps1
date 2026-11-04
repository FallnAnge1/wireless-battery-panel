param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $root 'src\Hi98Battery.cs'))
$match = [regex]::Match($source, 'AssemblyFileVersion\("(\d+\.\d+\.\d+)\.0"\)')
if (!$match.Success) { throw 'Could not determine source version' }
$version = $match.Groups[1].Value
$tag = 'v' + $version
$downloads = Join-Path $root 'downloads'
$exe = Join-Path $downloads 'WirelessBatteryPanel.exe'
$zip = Join-Path $downloads "wireless-battery-panel-$tag-win-x64.zip"
$sums = Join-Path $downloads 'SHA256SUMS.txt'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'Executable and source versions differ' }
$expected = @{}
foreach ($line in [IO.File]::ReadAllLines($sums)) {
    if ($line -match '^([a-fA-F0-9]{64})  (.+)$') { $expected[$Matches[2]] = $Matches[1] }
}
foreach ($file in @($exe, $zip)) {
    $name = [IO.Path]::GetFileName($file)
    if (!$expected.ContainsKey($name) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected[$name]) { throw "Release checksum mismatch: $name" }
}
if ($ValidateOnly) { Write-Output "PASS: $tag release files, versions and checksums"; return }
if (!$env:GITHUB_REPOSITORY -or !$env:GITHUB_SHA) { throw 'Run publication from the GitHub Actions release job' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$notes = Join-Path $dist 'RELEASE_NOTES.md'
$body = @"
## 下载使用
下载下方 Assets 中的 WirelessBatteryPanel.exe 可直接运行，或下载 Windows x64 ZIP 包。无需 Python；设置保存在用户配置目录。SHA256SUMS.txt 提供对应校验值。

## $tag 更新
新增通用标准 HID 电量读取，识别描述符声明的 Battery Strength / Relative State of Charge 字段，支持只读 Feature 和控制式 Input 报告。HI98 Pro、G3M PRO 仍优先使用已适配查询。

校验逻辑范围、无效值与多字段冲突；每接口最多一个未完成查询、全局最多四个，慢查询不阻塞面板。标准读取不会发送未知厂商写命令，也不会连续读取鼠标或键盘输入流。

## 验证范围
Windows 构建及硬件无关自检已通过，现有键盘、鼠标完成本机检查。标准字段的解析、冲突抑制、慢请求限流、失败恢复与面板集成通过测试。本机当前没有连接能成功上报标准电量的新设备，不能宣称全部品牌或新增型号已经实测。没有标准字段或不支持 GET_REPORT 的设备，仍可能显示 --。
"@
Set-Content -LiteralPath $notes -Value $body -Encoding utf8
# This only creates a new release. Existing versions/assets are never overwritten.
$releaseArgs = @('release', 'create', $tag, $exe, $zip, $sums, '--repo', $env:GITHUB_REPOSITORY, '--target', $env:GITHUB_SHA, '--title', "无线设备电量 $tag · 标准 HID 支持", '--notes-file', $notes, '--latest')
& gh @releaseArgs
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed; an existing version was not overwritten' }
