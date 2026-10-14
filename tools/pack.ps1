#requires -Version 7
<# Builds the installer and the portable exe. Usage (repo root): pwsh tools/pack.ps1 [-OutDir publish\v5]
   Needs the Velopack CLI once per machine: dotnet tool install -g vpk --version 1.2.0 #>
param([string]$OutDir = 'publish\v5')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$csproj = 'src/TaskbarCats/TaskbarCats.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $csproj" }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { throw 'vpk not found: dotnet tool install -g vpk --version 1.2.0' }

function Run([string]$exe, [string[]]$argv) { & $exe @argv; if ($LASTEXITCODE) { throw "$exe failed ($LASTEXITCODE)" } }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$app = Join-Path $OutDir 'app'

# Self-contained: strangers need no .NET runtime and no admin.
# packId is NOT TaskbarCats: Velopack deletes %LOCALAPPDATA%\<packId> on uninstall, and the hook events live in
# %LOCALAPPDATA%\TaskbarCats (AppInfo.PackId has the same value).
Run dotnet @('publish', 'src/TaskbarCats', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $app)
Run vpk @('pack', '--packId', 'TaskbarCats.App', '--packVersion', $version, '--packDir', $app,
    '--mainExe', 'TaskbarCats.exe', '--packTitle', 'taskbar-cats', '--packAuthors', 'tindll',
    '--icon', 'src/TaskbarCats/TaskbarCats.ico', '--shortcuts', 'StartMenuRoot', '--outputDir', $OutDir)

# Portable: one small exe for people who already have the .NET 10 Desktop Runtime.
Run dotnet @('publish', 'src/TaskbarCats', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false',
    '-p:PublishSingleFile=true', '-o', (Join-Path $OutDir 'portable'))

Remove-Item $app -Recurse -Force   # packed into the .nupkg; not a deliverable
Get-ChildItem $OutDir -Recurse -File | Where-Object Extension -in '.exe', '.nupkg', '.zip' |
    ForEach-Object { '{0,-70} {1,8:N1} MB' -f $_.FullName, ($_.Length / 1MB) }
