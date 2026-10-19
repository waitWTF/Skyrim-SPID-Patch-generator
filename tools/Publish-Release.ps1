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
if (Test-Path -LiteralPath $bundlePath) { throw 'A release folder with this timestamp already exists. Try again in a second.' }
New-Item -ItemType Directory -Path $appPath -Force | Out-Null
Push-Location $repoPath
try {
    if (-not $SkipChecks) {
        dotnet run --project tests\WearableDistributor.IntegrationChecks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed; no release was published.' }
    }
    dotnet publish src\WearableDistributor.csproj -c Release -r win-x64 --self-contained false -o $appPath -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    foreach ($file in @('README.md','RELEASE_NOTES.md','VERSION.txt','LICENSE','PROJECT.md','.gitignore','.gitattributes')) {
        if (Test-Path -LiteralPath (Join-Path $repoPath $file)) { Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination $bundlePath }
    }
    foreach ($folder in @('src','tests','tools','docs')) {
        $sourceRoot = Join-Path $repoPath $folder
        Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|preview-settings)[\\/]' -and
            $_.Extension -in @('.cs','.xaml','.csproj','.ico','.svg','.png','.md','.ps1','.json')
        } | ForEach-Object {
            $relative = [System.IO.Path]::GetRelativePath($repoPath, $_.FullName)
            $destination = Join-Path $bundlePath $relative
            New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
    }
    foreach ($file in @('LICENSE','VERSION.txt')) { Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination $appPath }
    @'
Skyrim SPID Patch Generator - Windows x64 preview

Extract this entire ZIP into a normal folder. Keep the EXE, DLLs, and JSON files together.
Requires Microsoft .NET 10 Desktop Runtime x64: https://dotnet.microsoft.com/download/dotnet/10.0
Start SkyrimSPIDPatchGenerator.exe. The program can be added as an executable tool in MO2 or Vortex.
For MO2, launch it through MO2 and scan virtual Data. For Vortex, scan deployed Data or staging.
Use a separate output mod folder. Enable the generated ESP, its required source plugins, and your chosen distribution engine.
Rescan/repatch after changing your NPC mods or profile. Generated recipient lists are a scan snapshot.
Do not put SPID and SkyPatcher output for this app in the same output folder.
Optional merchandise requires SkyPatcher even when NPC outfits use SPID; keep its generated container INI installed.
Enable SkyPatcher container patching (iEnableContainerPatching=1 in your installed SkyPatcher.ini) to use merchandise.
Existing WearableDistributor.esp/INI/settings names are retained for compatibility. Do not rename generated records/files by hand.
Source and documentation: https://github.com/waitWTF/Skyrim-SPID-Patch-generator
This follow-up was compiled. The new error-report and repatching flows were not runtime-tested for 0.4.6. See docs/ERROR_REPORTS_AND_REPATCHING.md and the release notes.
'@ | Set-Content -LiteralPath (Join-Path $appPath 'START-HERE.txt') -Encoding UTF8
    $appZip = Join-Path $outputPath "SkyrimSPIDPatchGenerator-v$version-preview-win-x64-$timestamp.zip"
    $sourceZip = "$bundlePath.zip"
    Compress-Archive -Path (Join-Path $appPath '*') -DestinationPath $appZip
    Compress-Archive -Path (Join-Path $bundlePath '*') -DestinationPath $sourceZip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($archivePath in @($appZip, $sourceZip)) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            if (-not ($archive.Entries | Where-Object { $_.FullName -match '(^|/)SkyrimSPIDPatchGenerator\.exe$' })) { throw "EXE missing from $archivePath" }
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

