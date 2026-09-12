param(
    # Defaults to GameDir in the untracked local.props, else Steam's standard install location.
    [string]$GameDir = '',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue) {
    throw 'Close Slay the Spire 2 before deploying (it locks RunRecap.dll).'
}
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$root = Split-Path -Parent $PSScriptRoot
if (-not $GameDir) {
    $props = Join-Path $root 'local.props'
    if (Test-Path $props) { $GameDir = ([xml](Get-Content $props -Raw)).Project.PropertyGroup.GameDir }
    if (-not $GameDir) { $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2' }
}
& $dotnet build "$root\src\RunRecap\RunRecap.csproj" -c $Configuration "-p:GameDir=$GameDir" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$out = "$root\src\RunRecap\bin\$Configuration\net9.0"
$dest = Join-Path $GameDir 'mods\RunRecap'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\RunRecap.dll", "$out\RunRecap.pdb", "$out\RunRecap.json" $dest -Force
Write-Host "Deployed to $dest"
