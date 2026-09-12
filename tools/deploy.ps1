param(
    # Both default to the untracked local.props (see local.props.example).
    [string]$GameDir = '',
    # A dotnet with the .NET 9 SDK; falls back to the one on PATH.
    [string]$Dotnet = '',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) {
    throw 'Close Slay the Spire 2 before deploying (it locks WhoCarried.dll).'
}
$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root 'local.props'
$local = if (Test-Path $props) { ([xml](Get-Content $props -Raw)).Project.PropertyGroup } else { $null }
if (-not $GameDir -and $local) { $GameDir = $local.GameDir }
if (-not $Dotnet -and $local) { $Dotnet = $local.DotnetPath }
if (-not $GameDir) { throw 'Set GameDir in local.props (copy local.props.example) or pass -GameDir.' }
if (-not $Dotnet) { $Dotnet = 'dotnet' }
& $Dotnet build "$root\src\WhoCarried\WhoCarried.csproj" -c $Configuration "-p:GameDir=$GameDir" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$out = "$root\src\WhoCarried\bin\$Configuration\net9.0"
$dest = Join-Path $GameDir 'mods\WhoCarried'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\WhoCarried.dll", "$out\WhoCarried.pdb", "$out\WhoCarried.json" $dest -Force
Write-Host "Deployed to $dest"
