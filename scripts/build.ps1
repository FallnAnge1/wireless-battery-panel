param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$compiler = Join-Path $framework 'csc.exe'
$output = Join-Path $dist 'WirelessBatteryPanel.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$icon = Join-Path $repoRoot 'assets\app.ico'
$references = @(
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    (Join-Path $metadata 'Windows.Devices.winmd'),
    (Join-Path $metadata 'Windows.Foundation.winmd'),
    (Join-Path $framework 'System.Runtime.dll')
)
if ($sources.Count -eq 0) { throw 'No C# source files found' }
foreach ($file in @($compiler, $icon) + $sources + $references[2..4]) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing required Windows component: $file" }
}
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001')
$compilerArgs += $references | ForEach-Object { '/r:' + $_ }
$compilerArgs += @(( '/win32icon:' + $icon ), ( '/out:' + $output )) + $sources
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
if ($Test) {
    $process = Start-Process -FilePath $output -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Self-tests failed: $($process.ExitCode)" }
    Write-Output 'PASS: protocol parsers, standard HID scaling/conflicts/slow-read recovery, charge states, receiver identity and offline lifecycle.'
}
Get-Item -LiteralPath $output | Select-Object Name, Length
