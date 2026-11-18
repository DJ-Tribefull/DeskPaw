$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'DeskPaw.exe'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /checked- /warnaserror+ /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll "/out:$target" $sources
if ($LASTEXITCODE -ne 0) { throw "DeskPaw compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $target | Select-Object FullName, Length
