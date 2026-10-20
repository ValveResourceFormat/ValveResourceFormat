import { defineConfig } from "vitepress";
import { readFileSync } from "fs";
import { resolve } from "path";
import { parseToc } from "./toc-parser.js";

const docsDir = resolve(import.meta.dirname, "..");
const siteUrl = "https://s2v.app/ValveResourceFormat/";
const siteDescription =
    "Source 2 resource file format parser, decompiler, and exporter";

const apiSidebar = parseToc(resolve(docsDir, "api/toc.yml"), "/api/");

// Split Renderer namespace into its own sidebar
let rendererSidebar = [];
const vrfRoot = apiSidebar[0];
if (vrfRoot?.items) {
    const rendererIndex = vrfRoot.items.findIndex(
        (item) => item.text === "Renderer",
    );
    if (rendererIndex !== -1) {
        rendererSidebar = [vrfRoot.items.splice(rendererIndex, 1)[0]];
    }
}

// The permalink label is built from the heading's markdown source, which still holds escapes
// such as \( and the {#id} suffix. Replacing the source with the parsed text cleans the label.
function cleanPermalinkLabels(md) {
    md.core.ruler.before("anchor", "clean_permalink_labels", (state) => {
        for (let i = 0; i < state.tokens.length - 1; i++) {
            if (state.tokens[i].type !== "heading_open") {
                continue;
            }

            const inline = state.tokens[i + 1];
            let text = "";
            for (const token of inline.children) {
                if (token.type === "text" || token.type === "code_inline") {
                    text += token.content;
                }
            }
            inline.content = text.trim();
        }
    });
}

// API pages without a summary hold only a signature, so the sitemap leaves them out. Generated
// pages get description frontmatter only when they have a summary.
function isListedInSitemap(item) {
    if (!item.url.startsWith("api/")) {
        return true;
    }

    const file = resolve(docsDir, item.url.replace(/\.html$/, ".md"));
    return readFileSync(file, "utf-8").startsWith("---\n");
}

export default defineConfig({
    title: "Source 2 Viewer",

    markdown: {
        config: cleanPermalinkLabels,
    },

    description: siteDescription,
    base: "/ValveResourceFormat/",

    themeConfig: {
        logo: {
            light: "/images/source2viewer.png",
            dark: "/images/source2viewer_dark.png",
        },

        nav: [
            { text: "Guides", link: "/guides/getting-started" },
            {
                text: "Library",
                link: "/api/ValveResourceFormat",
                activeMatch: "^/api/ValveResourceFormat(?!\\.Renderer)",
            },
            {
                text: "Renderer",
                link: "/api/ValveResourceFormat.Renderer",
                activeMatch: "^/api/ValveResourceFormat\\.Renderer",
            },
            { text: "Wiki", link: "https://www.source2.wiki" },
            {
                text: "Schema Explorer",
                link: "https://s2v.app/SchemaExplorer/",
            },
        ],

        sidebar: {
            "/api/ValveResourceFormat.Renderer": rendererSidebar,
            "/api/": apiSidebar,
            "/": [
                { text: "Introduction", link: "/" },
                { text: "Reporting Issues", link: "/guides/reporting-issues" },
                { text: "Help Write Guides", link: "/guides/contributing" },
                { text: "Privacy", link: "/guides/privacy" },
                {
                    text: "Source 2 Viewer",
                    items: [
                        {
                            text: "Getting Started",
                            link: "/guides/getting-started",
                        },
                        {
                            text: "Game Search Paths",
                            link: "/guides/game-search-paths",
                        },
                        {
                            text: "Format Support",
                            link: "/guides/format-support",
                        },
                        {
                            text: "Exporting Maps",
                            link: "/guides/exporting-maps",
                        },
                        {
                            text: "Exporting Models",
                            link: "/guides/exporting-models",
                        },
                        {
                            text: "Exporting Textures",
                            link: "/guides/exporting-textures",
                        },
                        {
                            text: "Exporting Sounds",
                            link: "/guides/exporting-sounds",
                        },
                        {
                            text: "Importing into Blender",
                            link: "/guides/importing-into-blender",
                        },
                        {
                            text: "VPK Management",
                            link: "/guides/vpk-management",
                        },
                    ],
                },
                {
                    text: "Games",
                    items: [
                        {
                            text: "Counter-Strike 2",
                            link: "/games/counter-strike-2",
                        },
                        {
                            text: "Dota 2",
                            link: "/games/dota-2",
                        },
                        {
                            text: "Deadlock",
                            link: "/games/deadlock",
                        },
                        {
                            text: "Half-Life: Alyx",
                            link: "/games/half-life-alyx",
                        },
                    ],
                },
                {
                    text: "Library & CLI",
                    items: [
                        {
                            text: "Valve Resource File",
                            link: "/guides/read-resource",
                        },
                        {
                            text: "Command-line Utility",
                            link: "/guides/command-line",
                        },
                        {
                            text: "MCP Automation Server",
                            link: "/guides/mcp-server",
                        },
                    ],
                },
            ],
        },

        outline: [2, 3],
        externalLinkIcon: true,

        search: {
            provider: "local",
            options: {
                miniSearch: {
                    searchOptions: {
                        // A page's top section has no parent headings. Ranking it higher puts a
                        // type's own page above its constructors and members of the same name.
                        boostDocument(id, term, storedFields) {
                            if (storedFields?.titles?.length === 0) {
                                return 2;
                            }

                            return 1;
                        },
                    },
                },
            },
        },

        socialLinks: [
            {
                icon: "github",
                link: "https://github.com/ValveResourceFormat/ValveResourceFormat",
            },
            { icon: "discord", link: "https://discord.gg/s9QQ7Wg7r4" },
        ],

        footer: {
            message:
                "This project is not affiliated with Valve Software. Source 2 is a trademark and/or registered trademark of Valve Corporation.",
        },

        editLink: {
            pattern:
                "https://github.com/ValveResourceFormat/ValveResourceFormat/edit/master/docs/:path",
        },
    },

    sitemap: {
        hostname: siteUrl,
        transformItems(items) {
            return items.filter(isListedInSitemap);
        },
    },

    transformPageData(pageData) {
        const canonicalUrl = `${siteUrl}${pageData.relativePath}`
            .replace(/index\.md$/, "")
            .replace(/\.md$/, ".html");
        const description = pageData.description || siteDescription;

        if (pageData.relativePath.startsWith("api/")) {
            pageData.frontmatter.pageClass = "api-page";
        }

        pageData.frontmatter.head ??= [];
        pageData.frontmatter.head.push(
            ["link", { rel: "canonical", href: canonicalUrl }],
            ["meta", { property: "og:type", content: "website" }],
            ["meta", { property: "og:site_name", content: "Source 2 Viewer" }],
            ["meta", { property: "og:title", content: pageData.title }],
            ["meta", { property: "og:description", content: description }],
            ["meta", { property: "og:url", content: canonicalUrl }],
            [
                "meta",
                {
                    property: "og:image",
                    content: `${siteUrl}images/source2viewer.png`,
                },
            ],
            ["meta", { name: "twitter:card", content: "summary" }],
        );
    },
});
