param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch] $SkipChecks
)
$ErrorActionPreference = 'Stop'
$repoPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$reviewPath = Join-Path $repoPath 'docs\DEPENDENCY_REVIEW.json'
if (-not (Test-Path -LiteralPath $reviewPath)) { throw 'Dependency distribution review is missing. Do not create a binary release without license/source review.' }
$distributionReview = Get-Content -LiteralPath $reviewPath -Raw | ConvertFrom-Json
if ($distributionReview.approved_for_binary_distribution -ne $true) {
    throw 'Binary release on hold: complete the license/corresponding-source work in docs/DEPENDENCY_REVIEW.json first. Ordinary source builds remain available.'
}
foreach ($requiredNotice in @('THIRD_PARTY_NOTICES.md','licenses\GPL-3.0.txt','docs\RESOLVED_DEPENDENCIES.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoPath $requiredNotice))) { throw "Missing required distribution notice: $requiredNotice" }
}

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
    dotnet publish src\WearableDistributor.csproj -c Release -r win-x64 --self-contained false -p:NuGetAudit=true -p:NuGetAuditMode=all -o $runtimePath "-p:ReleaseLayoutRoot=$appPath" -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
    foreach ($file in @('README.md','RELEASE_NOTES.md','VERSION.txt','LICENSE','PROJECT.md','THIRD_PARTY_NOTICES.md','START-HERE.txt','.gitignore','.gitattributes')) {
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
    foreach ($file in @('README.md','RELEASE_NOTES.md','LICENSE','VERSION.txt','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination $documentationPath }
    Copy-Item -LiteralPath (Join-Path $repoPath 'licenses') -Destination (Join-Path $bundlePath 'licenses') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoPath 'licenses') -Destination (Join-Path $documentationPath 'licenses') -Recurse
    Copy-Item -LiteralPath (Join-Path $bundlePath 'docs') -Destination (Join-Path $documentationPath 'docs') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoPath 'START-HERE.txt') -Destination $appPath
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

