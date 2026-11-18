$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDir = Join-Path $root 'artifacts\tests'
[void](New-Item -ItemType Directory -Path $testDir -Force)
$target = Join-Path $testDir 'Checks.exe'
$sources = @(
    (Join-Path $PSScriptRoot 'Checks.cs')
    (Join-Path $root 'source\InputGuard.cs')
    (Join-Path $root 'source\Preferences.cs')
)
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Security.dll "/out:$target" $sources
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed ($LASTEXITCODE)." }
& $target $testDir
if ($LASTEXITCODE -ne 0) { throw "DeskPause checks failed ($LASTEXITCODE)." }
