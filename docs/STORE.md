# Microsoft Store release workflow

PulseKey's free signing route uses a Microsoft Store MSIX. Microsoft signs an approved package for Store distribution. The existing GitHub portable EXE and ZIP remain unsigned; the Store signature cannot be transferred to them. No Azure subscription or paid certificate is needed for this route.

## Current status

- The name **PulseKey** is reserved for @BraxFFA in Partner Center (Store product ID `9NTRWC5CM8C5`).
- An unsigned submission MSIX has been built with the real Store identity. Store approval and signing happen only after submission and certification.

## Prepare the Store identity

1. Register for a free Microsoft Store developer account at [Store Developer](https://storedeveloper.microsoft.com/). Do not start an Azure subscription.
2. In Partner Center, reserve the app name **PulseKey** under **MSIX or PWA app**. If that name is unavailable, use a distinct name owned by @BraxFFA and pass it as `-StoreDisplayName` below.
3. Open **Product management → App identity** and copy **Package/Identity/Name** and **Package/Identity/Publisher** exactly. They are case sensitive. The publisher string may differ from the public @BraxFFA credit.

## Build the submission package

On 64-bit Windows with the Windows SDK `MakeAppx.exe` available, run from the repository root:

```powershell
.\scripts\package-store-msix.ps1 `
  -IdentityName '<Package/Identity/Name from Partner Center>' `
  -Publisher '<Package/Identity/Publisher from Partner Center>'
```

The script compiles the app from `src/PulseKey.cs`, creates Store tile PNGs, writes a full-trust desktop manifest, packages the result as `dist\PulseKey-v1.0.3-win-x64-store.msix`, checks its expected files, and prints a SHA-256 hash. The resulting **submission package is unsigned**. Do not attach it to GitHub as a trusted signed download.

When changing the app version, update the assembly and file versions in `src/PulseKey.cs` first; the package version comes from the built EXE. Keep the fourth package version field at `0` for Store submission.

## Submit and publish

1. In Partner Center, complete the submission draft, availability, and the Utilities + tools category.
2. Upload the MSIX from `dist`. Use the existing screenshots under `docs/images` for the listing. Describe PulseKey as an input studio and accurately disclose that it uses Windows `SendInput`; do not promise compatibility with protected games.
3. Run the Windows App Certification Kit and address any reported problems. Submit for Microsoft's certification.
4. After the Store listing is live, install it from the Store and verify the app starts, saves settings, opens the @BraxFFA credit, and performs an allowed test input. Then add the live Store listing link to the GitHub README and release notes.

The Store's signing applies to the Store delivery. If PulseKey is removed or rejected by Store certification, it is not signed by this workflow.
