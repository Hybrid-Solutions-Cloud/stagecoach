# Build and packaging scripts

All scripts are PowerShell 7 and live in `scripts/`.

| Script | Purpose |
|---|---|
| `Build.ps1` | Restore, build, and test the solution |
| `Run.ps1` | Build and launch the desktop application |
| `Package.ps1` | Produce the self-contained ZIP, its SHA-256 sidecar, and the WiX MSI |
| `Install-StagecoachShortcut.ps1` | Create a Start menu shortcut for a local build |
| `Start-StagecoachApp.ps1` | Launch an already-published build |

```powershell
pwsh ./scripts/Build.ps1 -Configuration Release
pwsh ./scripts/Package.ps1 -Configuration Release
```

Release verification, run in this order:

```powershell
dotnet build Stagecoach.sln -c Release          # must be zero warnings
dotnet test Stagecoach.sln -c Release
dotnet format Stagecoach.sln --verify-no-changes --no-restore
dotnet list Stagecoach.sln package --vulnerable --include-transitive
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, so a warning fails the build by design.

Artifacts land in `artifacts/` (ZIP plus checksum) and `installer/bin/Release/` (MSI).
