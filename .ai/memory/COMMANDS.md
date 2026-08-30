# Commands

```powershell
pwsh ./scripts/Build.ps1 -Configuration Release
pwsh ./scripts/Run.ps1 -Configuration Release
pwsh ./scripts/Package.ps1 -Version 0.1.0 -Installer
```

Release artifacts are written under `artifacts/`; the MSI is under `installer/bin/Release/`.
