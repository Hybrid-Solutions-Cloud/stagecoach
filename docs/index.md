---
layout: home

hero:
  name: Stagecoach
  text: One login. Every VM. One click.
  tagline: Local Entra ID-authenticated RDP/SSH launcher for Azure Bastion, Arc servers, and direct VMs.
  image:
    src: /images/stagecoach-icon.svg
    alt: Stagecoach wagon-wheel logo
  actions:
    - theme: brand
      text: Get Started
      link: /guide/quickstart
    - theme: alt
      text: Architecture
      link: /guide/architecture
    - theme: alt
      text: View on GitHub
      link: https://github.com/Hybrid-Solutions-Cloud/stagecoach

features:
  - icon: 🏰
    title: Azure Bastion Native Client
    details: Connect to Azure VMs behind Bastion with native mstsc.exe — Entra ID SSO and MFA included.
  - icon: 🌵
    title: Azure Arc & Azure Local
    details: 1-click RDP over SSH to Arc-enabled servers with automatic Active Directory Domain and Workgroup detection.
  - icon: 🔐
    title: Smart Credential Resolver
    details: Automatic secret lookup across Entra LAPS, AD Domain Accounts, and Azure Key Vault with zero disk persistence.
  - icon: ⚡
    title: Zero Infrastructure
    details: Localhost PowerShell 7 backend + single-file React interface. No cloud servers or Node.js build required.
---
