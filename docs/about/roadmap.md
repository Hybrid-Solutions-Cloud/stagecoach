# Roadmap

Planned features and delivery roadmap for Stagecoach.

---

## Completed Milestones

- [x] **Phase 0: Research Spikes & ADRs**
  - CLI connection mechanics validated for Bastion, Arc, and direct VMs (`SPIKE-001`).
  - Multi-tier credential resolver hierarchy defined (`SPIKE-002`).
  - Architecture decisions documented (`ADR-001`, `ADR-002`, `ADR-003`).
- [x] **Phase 1: Core Module & Web UI**
  - Standalone PowerShell 7 module (`AzureStagecoach`) with inventory, credential resolution, and launcher cmdlets.
  - Zero-dependency local web server (`Start-Stagecoach`).
  - Single-file React UI (`stagecoach.html`) with estate grid and domain detection.
  - Automated test coverage via Pester 6 and PSScriptAnalyzer.

---

## Upcoming Milestones

### Phase 2: Session Watchdog & Diagnostics
- [ ] Managed port pool for concurrent parallel tunnels (`127.0.0.1:50000+`).
- [ ] Active watchdog process to detect MSTSC exit and clean up orphaned background helper processes.
- [ ] Live stdout/stderr ring buffer for debugging connection failures.

### Phase 3: UX Polish & Preferences
- [ ] Opt-in "Save to Key Vault" secret writeback directly from the UI drawer.
- [ ] 30-second clipboard auto-clear timer when staging credentials.
- [ ] User preferences (`~/.stagecoach/config.json`) for default domain accounts and favorite VMs.

### Phase 4: Distribution & Packaging
- [ ] PowerShell Gallery publication (`Install-Module AzureStagecoach`).
- [ ] Winget manifest for workstation installation.
