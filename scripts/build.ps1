$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The 64-bit .NET Framework compiler was not found. Install the .NET Framework developer tools.'
}
$outputDirectory = Join-Path $repositoryRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$sourcePath = Join-Path $repositoryRoot 'src\PulseKey.cs'
$iconPath = Join-Path $repositoryRoot 'assets\PulseKey.ico'
$outputPath = Join-Path $outputDirectory 'PulseKey.exe'
& $compiler /nologo /target:winexe /platform:x64 "/win32icon:$iconPath" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll "/out:$outputPath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }
Write-Host "Built $outputPath"
