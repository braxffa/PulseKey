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

The assembly version is `1.0.0.0`; the package/release tag is `v1.0.0`. The executable is not code signed. Build on a trusted Windows machine and compare release checksums with the attached `SHA256SUMS.txt` if you download a prebuilt package.

## Manual smoke test

1. Launch the built executable and verify the three pages open.
2. Confirm the author link opens `https://linktr.ee/BraxFFA`.
3. Select an ordinary test app window, use a harmless key or mouse input, and verify **Idle → Armed → Active**. Confirm `F9` releases it.
4. Build, save, reload, edit, and run a small macro against a test app.
5. Switch themes, close and reopen PulseKey, and verify the choice persists.

Input behavior depends on the receiving application and cannot be proven from compilation alone. Do not use a protected game as a smoke-test target.
