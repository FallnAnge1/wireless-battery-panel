param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$compiler = Join-Path $framework 'csc.exe'
$output = Join-Path $dist 'WirelessBatteryPanel.exe'
$source = Join-Path $repoRoot 'src\Hi98Battery.cs'
$icon = Join-Path $repoRoot 'assets\app.ico'
$references = @(
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    (Join-Path $metadata 'Windows.Devices.winmd'),
    (Join-Path $metadata 'Windows.Foundation.winmd'),
    (Join-Path $framework 'System.Runtime.dll')
)
foreach ($file in @($compiler, $source, $icon) + $references[2..4]) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing required Windows component: $file" }
}
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+')
$compilerArgs += $references | ForEach-Object { '/r:' + $_ }
$compilerArgs += @(( '/win32icon:' + $icon ), ( '/out:' + $output ), $source)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
if ($Test) {
    $process = Start-Process -FilePath $output -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Self-tests failed: $($process.ExitCode)" }
    Write-Output 'PASS: protocol parsers, charge states, receiver identity and offline lifecycle.'
}
Get-Item -LiteralPath $output | Select-Object Name, Length
