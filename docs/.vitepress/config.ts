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

    nav: [{ text: 'Home', link: '/' }],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Hybrid-Solutions-Cloud/stagecoach' },
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Kristopher Turner / Hybrid Cloud Solutions',
    },
  },
})
