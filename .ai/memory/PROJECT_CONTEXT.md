# Project context

Stagecoach: local Entra ID-authenticated one-click RDP/SSH launcher for Azure
VMs behind Bastion, Arc-enabled servers (incl. Azure Local), and
direct-reachable Azure VMs. PS7 + Pode localhost backend (also a plain module);
single-file React frontend (vendored UMD + htm, no build step). Every connect
click spawns `pwsh` running `Connect-StagecoachVM`. Credential resolver:
Entra Windows LAPS → Key Vault → prompt. Background session manager keeps
tunnel/relay helpers hidden/minimized with watchdog + port pool.

Authoritative plan: `pmo/plans/stagecoach-design.md`.
