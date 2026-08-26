# Changelog

All notable changes to Stagecoach will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.0] - 2026-08-26

### Added
- **PowerShell Module:** `AzureStagecoach` PS7 module with `Get-StagecoachInventory`, `Get-StagecoachCredential`, `Connect-StagecoachVM`, and `Start-Stagecoach`.
- **Local Web Server:** Zero-dependency .NET `HttpListener` localhost server hosted on `127.0.0.1:8085`.
- **Single-File React UI:** Vendored React 18 + `htm` single-file frontend (`stagecoach.html`) with estate grid, Domain vs Workgroup badges, and slide-over connection drawer.
- **Credential Resolver:** Multi-tier secret lookup supporting Entra Windows LAPS, Active Directory Domain accounts, and Key Vault conventions.
- **Design Documentation:** Master implementation plan, research spikes (`SPIKE-001`, `SPIKE-002`), and ADRs (`ADR-001`, `ADR-002`, `ADR-003`).
- **Automated Tests:** Pester 6 unit test suites and PSScriptAnalyzer rules.
