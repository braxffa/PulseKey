# Build from source

## Requirements

- 64-bit Windows with the .NET Framework compiler (`csc.exe`) and the .NET Framework Windows Forms and Drawing assemblies.
- PowerShell for the included build script.
- No NuGet packages, Electron runtime, or external build dependencies.

From the repository root:

```powershell
.\scripts\build.ps1
```

The script writes `dist\PulseKey.exe`. It locates the 64-bit .NET Framework compiler under `%WINDIR%\Microsoft.NET\Framework64`. If you prefer a direct command:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /win32icon:assets\PulseKey.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll /out:dist\PulseKey.exe src\PulseKey.cs
```

The assembly version is `1.0.2.0`; the package/release tag is `v1.0.2`. The executable is not code signed. Build on a trusted Windows machine and compare release checksums with the attached `SHA256SUMS-v1.0.2.txt` if you download a prebuilt package.

## Signing a release

The published v1.0.2 downloads are unsigned. To produce a future signed release, use a validated **Public Trust** certificate profile in [Azure Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart), or an independently issued code-signing certificate. Artifact Signing requires an eligible paid Azure subscription, completed identity validation, and the Certificate Profile Signer role. Its [Basic plan](https://azure.microsoft.com/en-us/products/artifact-signing) has a monthly charge. Do not publish an executable as signed until verification succeeds.

Install [Microsoft's Artifact Signing Client Tools](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations) on the Windows signing machine:

```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
```

After building, run the included script with values from the Azure account and certificate profile:

```powershell
.\scripts\sign-artifact.ps1 `
  -Endpoint 'https://<region>.codesigning.azure.net' `
  -AccountName '<account-name>' `
  -CertificateProfileName '<profile-name>' `
  -InputPath '.\dist\PulseKey.exe' `
  -OutputPath '.\dist\PulseKey-signed.exe'
```

The script signs a **copy** with SHA-256 and a Microsoft timestamp, then requires successful SignTool and Windows Authenticode verification. A failed output is removed. Check the printed publisher name and SHA-256 hash before packaging. The account endpoint and profile name are configuration values; do not put Azure credentials or private signing keys in the repository. Recompute release checksums after signing because a signature changes the executable bytes.

For a local, non-admin setup, extract the official `Microsoft.ArtifactSigning.Client` NuGet package and .NET 8 runtime into a private working directory. Pass the x64 library path with `-DlibPath` and the runtime directory with `-DotNetRoot`; neither dependency needs to be committed to this repository.

## Manual smoke test

1. Launch the built executable and verify the three pages open.
2. Confirm the author link opens `https://linktr.ee/BraxFFA`.
3. Select an ordinary test app window, use a harmless key or mouse input, and verify **Idle → Armed → Active**. Confirm `F9` releases it.
4. Build, save, reload, edit, and run a small macro against a test app.
5. Switch themes, close and reopen PulseKey, and verify the choice persists.
6. Maximize the app and toggle `F11`; verify the cards, controls, text, sidebar, and header grow, then return to normal size on exit. Capture Shift in Macro studio and confirm the scaled timeline displays **Press Shift**.

Input behavior depends on the receiving application and cannot be proven from compilation alone. Do not use a protected game as a smoke-test target.
