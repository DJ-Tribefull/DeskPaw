$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'Cargo.toml'
$cargoDirectory = $env:CARGO_HOME
if (!$cargoDirectory) { $cargoDirectory = Join-Path $env:USERPROFILE '.cargo' }
$priorFlags = $env:CARGO_ENCODED_RUSTFLAGS
$separator = [char]31
$flags = @()
if ($priorFlags) { $flags += $priorFlags.Split($separator) }
elseif ($env:RUSTFLAGS) { $flags += ($env:RUSTFLAGS.Trim() -split '\s+') }
# Rust retains source locations for diagnostics even in stripped executables.
$flags += '--remap-path-prefix=' + [IO.Path]::GetFullPath($cargoDirectory) + '=/cargo'
$flags += '--remap-path-prefix=' + (Split-Path $PSScriptRoot -Parent) + '=/deskpause'
try {
    $env:CARGO_ENCODED_RUSTFLAGS = $flags -join $separator
    & cargo build --manifest-path $manifest --release --locked
    if ($LASTEXITCODE -ne 0) { throw "Settings compilation failed ($LASTEXITCODE)." }
} finally { $env:CARGO_ENCODED_RUSTFLAGS = $priorFlags }
$info = (& cargo metadata --manifest-path $manifest --locked --format-version 1 --no-deps | ConvertFrom-Json)
$binary = Join-Path $info.target_directory 'release\deskpause-settings.exe'
$portable = Join-Path (Split-Path $PSScriptRoot -Parent) 'DeskPause.Settings.exe'
Copy-Item -LiteralPath $binary -Destination $portable -Force
Get-Item -LiteralPath $portable | Select-Object FullName, Length
