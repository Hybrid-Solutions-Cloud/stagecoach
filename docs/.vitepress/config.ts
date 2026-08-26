import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Stagecoach',
  description:
    'One login. Every VM. One click. Entra ID-authenticated RDP/SSH launcher for Azure VMs behind Bastion, Arc-enabled servers, and direct-reachable VMs.',
  base: '/stagecoach/',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/stagecoach/images/stagecoach-icon.svg' }],
  ],

  themeConfig: {
    logo: '/images/stagecoach-icon.svg',

    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/guide/quickstart' },
      { text: 'Architecture', link: '/guide/architecture' },
      { text: 'Cmdlets', link: '/reference/cmdlets' },
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
            { text: 'Architecture', link: '/guide/architecture' },
            { text: 'Connection Routes', link: '/guide/connections' },
            { text: 'Credential Resolver', link: '/guide/credentials' },
          ],
        },
        {
          text: 'Architecture Decisions (ADRs)',
          items: [
            { text: 'ADR-001: Pode Backend', link: '/design/decisions/ADR-001-local-first-pode-backend' },
            { text: 'ADR-002: Credential Hierarchy', link: '/design/decisions/ADR-002-credential-resolution-hierarchy' },
            { text: 'ADR-003: Session Lifecycle', link: '/design/decisions/ADR-003-background-session-lifecycle' },
          ],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Cmdlets', link: '/reference/cmdlets' },
          ],
        },
      ],
      '/design/': [
        {
          text: 'Architecture Decisions (ADRs)',
          items: [
            { text: 'ADR-001: Pode Backend', link: '/design/decisions/ADR-001-local-first-pode-backend' },
            { text: 'ADR-002: Credential Hierarchy', link: '/design/decisions/ADR-002-credential-resolution-hierarchy' },
            { text: 'ADR-003: Session Lifecycle', link: '/design/decisions/ADR-003-background-session-lifecycle' },
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
