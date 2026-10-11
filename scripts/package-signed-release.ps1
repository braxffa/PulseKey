param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $SignedExePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $SignedExePath).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$signature = Get-AuthenticodeSignature -LiteralPath $source
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
    throw 'The executable must have a valid, timestamped Authenticode signature.'
}

$fileVersion = (Get-Item -LiteralPath $source).VersionInfo.FileVersion
if ($fileVersion -ne "$Version.0") {
    throw "File version $fileVersion does not match release version $Version."
}

$baseName = "PulseKey-v$Version-win-x64"
$exeOutput = Join-Path $outputRoot "$baseName.exe"
$zipOutput = Join-Path $outputRoot "$baseName.zip"
$checksumsOutput = Join-Path $outputRoot "SHA256SUMS-v$Version.txt"
foreach ($path in @($exeOutput, $zipOutput, $checksumsOutput)) {
    if (Test-Path -LiteralPath $path) { throw "Output already exists: $path" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$zip = $null
$complete = $false
try {
    Copy-Item -LiteralPath $source -Destination $exeOutput
    $zip = [IO.Compression.ZipFile]::Open($zipOutput, [IO.Compression.ZipArchiveMode]::Create)
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, 'PulseKey.exe', [IO.Compression.CompressionLevel]::Optimal)

    $fixedFiles = @('README.md', 'CHANGELOG.md', 'LICENSE', 'SECURITY.md',
        'assets/PulseKey.ico', 'scripts/build.ps1', 'scripts/sign-artifact.ps1',
        'scripts/package-signed-release.ps1', 'src/PulseKey.cs')
    $documentFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'docs') -Recurse -File |
        ForEach-Object { $_.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/') }
    foreach ($relativePath in @($fixedFiles) + @($documentFiles)) {
        $file = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing package file: $relativePath" }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $relativePath.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
    }
    $zip.Dispose()
    $zip = $null

    $checksumLines = @($exeOutput, $zipOutput) | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $_)
    }
    [IO.File]::WriteAllLines($checksumsOutput, $checksumLines, [Text.UTF8Encoding]::new($false))
    $complete = $true
    Write-Output "Signed release package ready: $outputRoot"
    Write-Output ($checksumLines -join [Environment]::NewLine)
}
finally {
    if ($zip) { $zip.Dispose() }
    if (-not $complete) {
        foreach ($path in @($exeOutput, $zipOutput, $checksumsOutput)) {
            Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
        }
    }
}
