param(
    [string] $GameDir = (Join-Path $PSScriptRoot '..\..'),
    [string] $OutDir = ''
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path.TrimEnd('\', '/') + '/'
if (-not $OutDir) { $OutDir = Join-Path $gameRoot 'Thunderstore-Packages/release-assets' }
$version = '1.2.2'
dotnet build (Join-Path $PSScriptRoot 'Propeller.csproj') -c Release --nologo -v:minimal "-p:GameDir=$gameRoot" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Propeller build failed.' }
$dll = Join-Path $PSScriptRoot 'bin/Release/netstandard2.0/Propeller.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne $version) { throw 'Unexpected assembly version.' }
$asset = Join-Path $gameRoot 'BepInEx/plugins/Propeller/assets/dogeggz.jetpump.assets'
if (-not (Test-Path -LiteralPath $asset)) { throw 'The existing Propeller asset bundle must be installed.' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zipPath = Join-Path $OutDir "Propeller-$version.zip"
if (Test-Path -LiteralPath $zipPath) { throw "Release archive already exists: $zipPath" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$entries = [ordered]@{
    'Propeller/Propeller.dll' = $dll
    'Propeller/assets/dogeggz.jetpump.assets' = $asset
    'Propeller/README.md' = (Join-Path $PSScriptRoot 'README.md')
    'Propeller/CHANGELOG.md' = (Join-Path $PSScriptRoot 'CHANGELOG.md')
}
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($entry in $entries.GetEnumerator()) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key) | Out-Null
    }
} finally { $zip.Dispose() }
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
