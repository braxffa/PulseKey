param(
    [Parameter(Mandatory = $true)][string]$IdentityName,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [string]$StoreDisplayName = 'PulseKey',
    [string]$PublisherDisplayName = 'BraxFFA',
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$stage = Join-Path $root 'dist\store-msix-stage'
$assets = Join-Path $stage 'Assets'
$exe = Join-Path $root 'dist\PulseKey.exe'

if ([string]::IsNullOrWhiteSpace($IdentityName) -or [string]::IsNullOrWhiteSpace($Publisher)) {
    throw 'Use the exact Package/Identity/Name and Package/Identity/Publisher values from Partner Center.'
}

& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) { throw 'PulseKey build failed.' }
$version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Unexpected executable version: $version" }
$releaseVersion = $version -replace '\.0$', ''

New-Item -ItemType Directory -Force -Path $assets | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'PulseKey.exe') -Force

# The Store tiles reproduce the existing blue PulseKey icon as vector strokes.
Add-Type -AssemblyName System.Drawing
function New-StoreTile([int]$size, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, $size, $size)),
        ([System.Drawing.Color]::FromArgb(255, 84, 154, 229)),
        ([System.Drawing.Color]::FromArgb(255, 103, 210, 225)),
        15.0
    )
    $line = New-Object System.Drawing.Pen(([System.Drawing.Color]::FromArgb(255, 232, 246, 255)), [Math]::Max(1.7, $size * 0.038))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.FillRectangle($background, 0, 0, $size, $size)
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($size * 0.15, $size * 0.52),
            [System.Drawing.PointF]::new($size * 0.36, $size * 0.52),
            [System.Drawing.PointF]::new($size * 0.44, $size * 0.30),
            [System.Drawing.PointF]::new($size * 0.53, $size * 0.73),
            [System.Drawing.PointF]::new($size * 0.61, $size * 0.52),
            [System.Drawing.PointF]::new($size * 0.84, $size * 0.52)
        )
        $graphics.DrawLines($line, $points)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $line.Dispose()
        $background.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
New-StoreTile 50 (Join-Path $assets 'StoreLogo.png')
New-StoreTile 44 (Join-Path $assets 'Square44x44Logo.png')
New-StoreTile 150 (Join-Path $assets 'Square150x150Logo.png')

function Escape-Xml([string]$value) {
    return [System.Security.SecurityElement]::Escape($value)
}
$identityXml = Escape-Xml $IdentityName
$publisherXml = Escape-Xml $Publisher
$displayXml = Escape-Xml $StoreDisplayName
$publisherDisplayXml = Escape-Xml $PublisherDisplayName
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
  <Identity Name="$identityXml" Publisher="$publisherXml" Version="$version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>$displayXml</DisplayName>
    <PublisherDisplayName>$publisherDisplayXml</PublisherDisplayName>
    <Description>PulseKey Windows input studio</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Resources>
    <Resource Language="en-US" />
  </Resources>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
  <Applications>
    <Application Id="PulseKey" Executable="PulseKey.exe"
                 uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
      <uap:VisualElements DisplayName="$displayXml" Description="PulseKey Windows input studio"
                          Square150x150Logo="Assets\Square150x150Logo.png"
                          Square44x44Logo="Assets\Square44x44Logo.png"
                          BackgroundColor="#13243F" />
    </Application>
  </Applications>
</Package>
"@
$manifestPath = Join-Path $stage 'AppxManifest.xml'
[xml]$parsed = $manifest
$parsed.Save($manifestPath)

$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeAppx = Get-ChildItem -LiteralPath $sdkRoot -Filter makeappx.exe -Recurse -ErrorAction Stop |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeAppx) { throw 'Windows SDK MakeAppx.exe x64 was not found.' }
if (-not $OutputPath) { $OutputPath = Join-Path $root "dist\PulseKey-v$releaseVersion-win-x64-store.msix" }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
& $makeAppx.FullName pack /o /d $stage /p $OutputPath
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($OutputPath)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @('AppxManifest.xml', 'PulseKey.exe', 'Assets/StoreLogo.png',
                            'Assets/Square44x44Logo.png', 'Assets/Square150x150Logo.png')) {
        if ($required -notin $entries) { throw "MSIX is missing $required" }
    }
} finally {
    $archive.Dispose()
}
$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
Write-Output "Store submission package: $OutputPath"
Write-Output "Package version: $version"
Write-Output "SHA-256: $hash"
Write-Output 'This submission package is unsigned. Microsoft signs approved MSIX Store distributions.'
