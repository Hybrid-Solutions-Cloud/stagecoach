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
      {
        text: 'About',
        items: [
          { text: 'About Stagecoach', link: '/about/' },
          { text: 'Roadmap', link: '/about/roadmap' },
          { text: 'Changelog', link: '/about/changelog' },
          { text: 'Release notes', link: '/about/releases' },
        ],
      },
    ],

    sidebar: {
      '/about/': [
        {
          text: 'About',
          items: [
            { text: 'About Stagecoach', link: '/about/' },
            { text: 'Roadmap', link: '/about/roadmap' },
            { text: 'Changelog', link: '/about/changelog' },
            { text: 'Release notes', link: '/about/releases' },
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
