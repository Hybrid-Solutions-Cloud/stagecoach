import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Stagecoach',
  description:
    'One identity hub. Every reachable machine. One click. A native Windows launcher for RDP and SSH to Azure VMs behind Bastion, Azure Arc-enabled servers, and Azure Local machines.',
  base: '/stagecoach/',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/stagecoach/images/stagecoach-icon.svg' }],
  ],

  themeConfig: {
    logo: '/images/stagecoach-icon.svg',

    nav: [
      { text: 'Home', link: '/' },
      { text: 'Download', link: '/download' },
      { text: 'Guide', link: '/guide/quickstart' },
      { text: 'Interface', link: '/guide/interface' },
      { text: 'Architecture', link: '/guide/architecture' },
      {
        text: 'About',
        items: [
          { text: 'About Stagecoach', link: '/about/' },
          { text: 'Roadmap', link: '/about/roadmap' },
          { text: 'Changelog', link: '/about/changelog' },
          { text: 'Release Notes', link: '/about/releases' },
        ],
      },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Getting Started',
          items: [
            { text: 'Quickstart', link: '/guide/quickstart' },
            { text: 'The interface', link: '/guide/interface' },
            { text: 'Local accounts', link: '/guide/credentials' },
            { text: 'Connection routes', link: '/guide/connections' },
            { text: 'Updating Stagecoach', link: '/guide/updates' },
            { text: 'Architecture', link: '/guide/architecture' },
          ],
        },
        {
          text: 'Architecture Decisions (ADRs)',
          items: [
            { text: 'ADR-001: Native Windows application', link: '/design/decisions/ADR-001-native-windows-application' },
            { text: 'ADR-002: Identity and credential separation', link: '/design/decisions/ADR-002-identity-and-credential-separation' },
            { text: 'ADR-003: Managed session lifecycle', link: '/design/decisions/ADR-003-managed-session-lifecycle' },
            { text: 'ADR-004: Metadata cache and identity hub', link: '/design/decisions/ADR-004-persistent-metadata-cache-and-identity-hub' },
            { text: 'ADR-005: Pinned local accounts', link: '/design/decisions/ADR-005-pinned-local-accounts-and-single-arc-identity' },
            { text: 'ADR-006: In-app updates', link: '/design/decisions/ADR-006-in-app-updates' },
          ],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Build and packaging scripts', link: '/reference/scripts' },
          ],
        },
      ],
      '/design/': [
        {
          text: 'Architecture Decisions (ADRs)',
          items: [
            { text: 'ADR-001: Native Windows application', link: '/design/decisions/ADR-001-native-windows-application' },
            { text: 'ADR-002: Identity and credential separation', link: '/design/decisions/ADR-002-identity-and-credential-separation' },
            { text: 'ADR-003: Managed session lifecycle', link: '/design/decisions/ADR-003-managed-session-lifecycle' },
            { text: 'ADR-004: Metadata cache and identity hub', link: '/design/decisions/ADR-004-persistent-metadata-cache-and-identity-hub' },
            { text: 'ADR-005: Pinned local accounts', link: '/design/decisions/ADR-005-pinned-local-accounts-and-single-arc-identity' },
            { text: 'ADR-006: In-app updates', link: '/design/decisions/ADR-006-in-app-updates' },
          ],
        },
      ],
      '/about/': [
        {
          text: 'About',
          items: [
            { text: 'About Stagecoach', link: '/about/' },
            { text: 'Roadmap', link: '/about/roadmap' },
            { text: 'Changelog', link: '/about/changelog' },
            { text: 'Release Notes', link: '/about/releases' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Hybrid-Solutions-Cloud/stagecoach' },
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Kristopher Turner / Hybrid Cloud Solutions',
    },
  },
})
