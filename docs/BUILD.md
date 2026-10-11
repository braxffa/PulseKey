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

## Free Microsoft Store package signing

The published v1.0.2 GitHub EXE and ZIP are unsigned. The free route is a **Microsoft Store MSIX**: after the app passes Store certification, Microsoft signs the Store-distributed package. This does not sign the standalone EXE or a copy of the MSIX downloaded from GitHub. See [Store release workflow](STORE.md) for the exact steps and current status.

The repository contains `scripts/package-store-msix.ps1` for preparing the unsigned submission package. It takes the exact package name and publisher identity from Partner Center and validates the generated MSIX. It does not require Azure or the Artifact Signing Client Tools.

## Manual smoke test

1. Launch the built executable and verify the three pages open.
2. Confirm the author link opens `https://linktr.ee/BraxFFA`.
3. Select an ordinary test app window, use a harmless key or mouse input, and verify **Idle → Armed → Active**. Confirm `F9` releases it.
4. Build, save, reload, edit, and run a small macro against a test app.
5. Switch themes, close and reopen PulseKey, and verify the choice persists.
6. Maximize the app and toggle `F11`; verify the cards, controls, text, sidebar, and header grow, then return to normal size on exit. Capture Shift in Macro studio and confirm the scaled timeline displays **Press Shift**.

Input behavior depends on the receiving application and cannot be proven from compilation alone. Do not use a protected game as a smoke-test target.
