# Сборка Screenshoter.exe встроенным csc.exe (.NET Framework 4.x) — без установок.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe не найден: $csc" }

$src = Join-Path $root 'Screenshoter.cs'
$out = Join-Path $root 'Screenshoter.exe'

& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $src

if ($LASTEXITCODE -ne 0) { throw "Сборка упала (код $LASTEXITCODE)" }
Write-Host "OK -> $out"
