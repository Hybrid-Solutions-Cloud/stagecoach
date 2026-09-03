---
layout: home

hero:
  name: Stagecoach
  text: One identity hub. Every reachable machine. One click.
  tagline: A native Windows launcher for RDP and SSH to Azure VMs behind Bastion, Azure Arc-enabled servers, and Azure Local machines.
  actions:
    - theme: brand
      text: Download for Windows
      link: /download
    - theme: alt
      text: Quickstart
      link: /guide/quickstart
    - theme: alt
      text: The interface
      link: /guide/interface
    - theme: alt
      text: GitHub
      link: https://github.com/Hybrid-Solutions-Cloud/stagecoach

features:
  - title: Opens on your machines
    details: The app lands directly on a filterable list of every Azure VM, Arc server, and Azure Local machine your accounts can reach. Filter by tenant, subscription, source, OS, and state.
  - title: One click, nothing visible
    details: Pin a local account to a machine and Connect launches it. The Azure CLI helper runs hidden, then Remote Desktop opens. No console window, no credential prompt.
  - title: Arc behaves like everything else
    details: Arc Remote Desktop relays over SSH using the same single local account for both hops. You are never asked to enter a local administrator account for Arc.
  - title: Several Entra accounts at once
    details: Each account gets its own isolated, Windows-encrypted Azure CLI session. One combined estate, and one account's expired session never blocks the others.
  - title: Passwords stay in Windows
    details: Local account passwords live in Windows Credential Manager and are read at launch only. Never in the database, logs, command lines, or .rdp files.
  - title: Updates itself
    details: Checks the signed release feed, verifies the installer against its authenticated digest twice, then hands it to Windows Installer.
---
