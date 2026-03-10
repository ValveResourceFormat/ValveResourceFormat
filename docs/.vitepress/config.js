import { defineConfig } from 'vitepress'
import { resolve } from 'path'
import { parseToc } from './toc-parser.js'

const docsDir = resolve(import.meta.dirname, '..')

const apiSidebar = parseToc(resolve(docsDir, 'api/toc.yml'), '/api/')

// Split Renderer namespace into its own sidebar
const vrfRoot = apiSidebar[0]
const rendererIndex = vrfRoot.items.findIndex(item => item.text === 'Renderer')
const rendererSidebar = [vrfRoot.items.splice(rendererIndex, 1)[0]]

export default defineConfig({
    title: 'Source 2 Viewer',
    description: 'Source 2 resource file format parser, decompiler, and exporter',
    base: '/ValveResourceFormat/',

    themeConfig: {
        logo: {
            light: '/images/source2viewer.png',
            dark: '/images/source2viewer_dark.png',
        },

        nav: [
            { text: 'Guides', link: '/guides/read-resource' },
            { text: 'Library', link: '/api/ValveResourceFormat' },
            { text: 'Renderer', link: '/api/ValveResourceFormat.Renderer' },
            { text: 'Viewer', link: 'https://s2v.app' },
            { text: 'Wiki', link: 'https://www.source2.wiki' },
        ],

        sidebar: {
            '/api/ValveResourceFormat.Renderer': rendererSidebar,
            '/api/': apiSidebar,
            '/': [
                { text: 'Introduction', link: '/' },
                { text: 'Valve Resource File', link: '/guides/read-resource' },
                { text: 'Command-line utility', link: '/guides/command-line' },
                { text: 'Help Write Guides', link: '/guides/contributing' },
            ],
        },

        outline: [2, 3],
        externalLinkIcon: true,

        search: {
            provider: 'local',
        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/ValveResourceFormat/ValveResourceFormat' },
            { icon: 'discord', link: 'https://discord.gg/s9QQ7Wg7r4' },
        ],

        footer: {
            message: 'This project is not affiliated with Valve Software. Source 2 is a trademark and/or registered trademark of Valve Corporation.',
            copyright: 'Released under the MIT License.',
        },

        editLink: {
            pattern: 'https://github.com/ValveResourceFormat/ValveResourceFormat/edit/master/docs/:path',
        },
    },

    sitemap: {
        hostname: 'https://s2v.app/ValveResourceFormat/',
    },

})
