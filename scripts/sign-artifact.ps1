param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://[a-z0-9-]+\.codesigning\.azure\.net/?$')]
    [string] $Endpoint,

    [Parameter(Mandatory = $true)]
    [string] $AccountName,

    [Parameter(Mandatory = $true)]
    [string] $CertificateProfileName,

    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $DlibPath,

    [string] $DotNetRoot
)

$ErrorActionPreference = 'Stop'

# Keep the unsigned build intact. Only a verified, timestamped copy is a release artifact.
$source = (Resolve-Path -LiteralPath $InputPath).Path
$destination = [IO.Path]::GetFullPath($OutputPath)
if ([string]::Equals($source, $destination, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'InputPath and OutputPath must differ.'
}
if (Test-Path -LiteralPath $destination) {
    throw "Output already exists: $destination"
}
if (-not $DlibPath) {
    $clientTools = "${env:ProgramFiles(x86)}\Microsoft\ArtifactSigningClientTools\bin"
    $DlibPath = @(
        (Join-Path $clientTools 'Azure.CodeSigning.Dlib.dll'),
        (Join-Path $clientTools 'x64\Azure.CodeSigning.Dlib.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $DlibPath -or -not (Test-Path -LiteralPath $DlibPath)) {
    throw 'Artifact Signing client library not found. Install Microsoft.Azure.ArtifactSigningClientTools or pass -DlibPath.'
}

$signTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction Stop |
    Where-Object { $_.DirectoryName -match '\\x64$' } |
    Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
    Select-Object -First 1
if (-not $signTool) { throw 'Windows SDK SignTool x64 was not found.' }

$metadataPath = Join-Path ([IO.Path]::GetTempPath()) ("pulsekey-artifact-signing-{0}.json" -f [guid]::NewGuid())
$metadata = @{
    Endpoint = $Endpoint.TrimEnd('/')
    CodeSigningAccountName = $AccountName
    CertificateProfileName = $CertificateProfileName
} | ConvertTo-Json -Compress

$signed = $false
$previousDotNetRoot = $env:DOTNET_ROOT
$previousDotNetRootX64 = $env:DOTNET_ROOT_X64
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
try {
    if ($DotNetRoot) {
        if (-not (Test-Path -LiteralPath (Join-Path $DotNetRoot 'host\fxr'))) {
            throw "No .NET host was found at: $DotNetRoot"
        }
        $env:DOTNET_ROOT = (Resolve-Path -LiteralPath $DotNetRoot).Path
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    }
    Copy-Item -LiteralPath $source -Destination $destination
    [IO.File]::WriteAllText($metadataPath, $metadata, [Text.UTF8Encoding]::new($false))
    & $signTool.FullName sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /d PulseKey /du 'https://github.com/braxffa/PulseKey' /dlib $DlibPath /dmdf $metadataPath $destination
    if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed with exit code $LASTEXITCODE" }

    & $signTool.FullName verify /pa /v $destination
    if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed with exit code $LASTEXITCODE" }

    $signature = Get-AuthenticodeSignature -LiteralPath $destination
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) {
        throw "Authenticode verification failed: $($signature.Status)"
    }
    if (-not $signature.TimeStamperCertificate) {
        throw 'The signature has no trusted timestamp.'
    }

    $signed = $true
    Write-Output "Signed and verified: $destination"
    Write-Output "Publisher: $($signature.SignerCertificate.Subject)"
    Write-Output "SHA-256: $((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant())"
}
finally {
    $env:DOTNET_ROOT = $previousDotNetRoot
    $env:DOTNET_ROOT_X64 = $previousDotNetRootX64
    Remove-Item -LiteralPath $metadataPath -ErrorAction SilentlyContinue
    if (-not $signed -and (Test-Path -LiteralPath $destination)) {
        Remove-Item -LiteralPath $destination -ErrorAction SilentlyContinue
    }
}
