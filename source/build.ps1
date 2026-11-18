$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'DeskPause.exe'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /checked- /warnaserror+ /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll "/out:$target" $sources
if ($LASTEXITCODE -ne 0) { throw "DeskPause compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $target | Select-Object FullName, Length
