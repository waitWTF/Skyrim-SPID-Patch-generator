param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch] $SkipChecks
)
$ErrorActionPreference = 'Stop'
$repoPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[xml] $projectMetadata = Get-Content -LiteralPath (Join-Path $repoPath 'src\WearableDistributor.csproj') -Raw
$version = $projectMetadata.Project.PropertyGroup.Version | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric application version in the project.' }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bundlePath = Join-Path $outputPath "SkyrimSPIDPatchGenerator-GitHub-v$version-preview-$timestamp"
$appPath = Join-Path $bundlePath 'app'
$runtimePath = Join-Path $appPath 'Runtime'
$documentationPath = Join-Path $appPath 'Documentation'
if (Test-Path -LiteralPath $bundlePath) { throw 'A release folder with this timestamp already exists. Try again in a second.' }
New-Item -ItemType Directory -Path $runtimePath, $documentationPath -Force | Out-Null

# Write a single friendly top-level directory without copying a second payload.
# Enumerating files explicitly also includes dotfiles in the developer bundle.
function New-ReleaseArchive([string] $SourceDirectory, [string] $DestinationPath, [string] $RootName) {
    $archive = [System.IO.Compression.ZipFile]::Open($DestinationPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse -Force) {
            $relative = [System.IO.Path]::GetRelativePath($SourceDirectory, $file.FullName).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$RootName/$relative", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
}
Push-Location $repoPath
try {
    if (-not $SkipChecks) {
        dotnet run --project tests\WearableDistributor.IntegrationChecks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed; no release was published.' }
    }
    dotnet publish src\WearableDistributor.csproj -c Release -r win-x64 --self-contained false -o $runtimePath "-p:ReleaseLayoutRoot=$appPath" -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    foreach ($file in @('README.md','RELEASE_NOTES.md','VERSION.txt','LICENSE','PROJECT.md','.gitignore','.gitattributes')) {
        if (Test-Path -LiteralPath (Join-Path $repoPath $file)) { Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination $bundlePath }
    }
    foreach ($folder in @('src','tests','tools','docs')) {
        $sourceRoot = Join-Path $repoPath $folder
        Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|preview-settings)[\\/]' -and
            $_.Extension -in @('.cs','.xaml','.csproj','.targets','.props','.ico','.svg','.png','.md','.ps1','.json')
        } | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($repoPath, $_.FullName)
            $destination = Join-Path $bundlePath $relative
            New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
    }
    foreach ($file in @('README.md','RELEASE_NOTES.md','LICENSE','VERSION.txt')) { Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination $documentationPath }
    Copy-Item -LiteralPath (Join-Path $bundlePath 'docs') -Destination (Join-Path $documentationPath 'docs') -Recurse
    @'
Skyrim SPID Patch Generator - Windows x64 preview

Extract the whole "Skyrim SPID Patch Generator" folder. Do not copy only the EXE.
SkyrimSPIDPatchGenerator.exe is at the top; keep its Runtime and Documentation folders beside it.
Runtime contains required DLL/JSON files. Documentation contains README, release notes, license and guides.
Requires Microsoft .NET 10 Desktop Runtime x64: https://dotnet.microsoft.com/download/dotnet/10.0
Start SkyrimSPIDPatchGenerator.exe. The program can be added as an executable tool in MO2 or Vortex.
In MO2, select the top-level EXE as Binary and its containing folder as Start in.
When updating from an older flat download, extract into a fresh folder and update the manager's EXE path.
Settings, patch history and existing generated output remain in their current locations.
For MO2, launch it through MO2 and scan virtual Data. For Vortex, scan deployed Data or staging.
Use a separate output mod folder. Enable the generated ESP, its required source plugins, and your chosen distribution engine.
Rescan/repatch after changing your NPC mods or profile. Generated recipient lists are a scan snapshot.
Do not put SPID and SkyPatcher output for this app in the same output folder.
Optional merchandise requires SkyPatcher even when NPC outfits use SPID; keep its generated container INI installed.
Only want shop stock? Tick only Sell selected parts through traders and leave NPC distribution unticked. No NPC audience or chance is needed.
Enable SkyPatcher container patching (iEnableContainerPatching=1 in your installed SkyPatcher.ini) to use merchandise.
Existing WearableDistributor.esp/INI/settings names are retained for compatibility. Do not rename generated records/files by hand.
Source and documentation: https://github.com/waitWTF/Skyrim-SPID-Patch-generator
Build verification only; this layout has not been launch-tested directly or through MO2/Vortex. See Documentation/RELEASE_NOTES.md.
'@ | Set-Content -LiteralPath (Join-Path $appPath 'START-HERE.txt') -Encoding UTF8
    $appZip = Join-Path $outputPath "SkyrimSPIDPatchGenerator-v$version-preview-win-x64-$timestamp.zip"
    $sourceZip = "$bundlePath.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-ReleaseArchive $appPath $appZip 'Skyrim SPID Patch Generator'
    New-ReleaseArchive $bundlePath $sourceZip 'Skyrim SPID Patch Generator - Source'
    foreach ($archivePath in @($appZip, $sourceZip)) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            if (-not ($archive.Entries | Where-Object { $_.FullName -match '(^|/)SkyrimSPIDPatchGenerator\.exe$' })) { throw "EXE missing from $archivePath" }
            if (-not ($archive.Entries | Where-Object { $_.FullName -match '/Runtime/SkyrimSPIDPatchGenerator\.runtimeconfig\.json$' })) { throw "Runtime configuration missing from $archivePath" }
            if (-not ($archive.Entries | Where-Object { $_.FullName -match '/Runtime/SkyrimSPIDPatchGenerator\.dll$' })) { throw "Managed entry point missing from $archivePath" }
            if (-not ($archive.Entries | Where-Object { $_.FullName -match '/Documentation/README\.md$' })) { throw "User documentation missing from $archivePath" }
            foreach ($entry in $archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') }) {
                $stream = $entry.Open()
                try { $stream.CopyTo([System.IO.Stream]::Null) } finally { $stream.Dispose() }
            }
        } finally { $archive.Dispose() }
    }
    Get-FileHash -LiteralPath $appZip, $sourceZip -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $outputPath "SkyrimSPIDPatchGenerator-v$version-preview-$timestamp-SHA256.txt") -Encoding UTF8
    Write-Output "App ZIP: $appZip"
    Write-Output "Source ZIP: $sourceZip"
    Write-Output "Source folder: $bundlePath"
} finally { Pop-Location }

