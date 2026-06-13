import { readFileSync, existsSync } from "fs";
import { resolve, dirname } from "path";
import yaml from "js-yaml";

/**
 * Parse a DocFX toc.yml file into VitePress sidebar items.
 * @param {string} tocPath - Absolute path to the toc.yml file
 * @param {string} baseUrl - URL prefix for links (e.g. '/api/' or '/guides/')
 */
export function parseToc(tocPath, baseUrl) {
    if (!existsSync(tocPath)) return [];

    let content = readFileSync(tocPath, "utf-8");

    // Strip DocFX YAML MIME header if present
    content = content.replace(/^### YamlMime:\w+\n/, "");

    const tocDir = dirname(tocPath);
    const items = yaml.load(content);
    if (!Array.isArray(items)) return [];

    return convertItems(items, baseUrl, tocDir);
}

function readTitleFromMarkdown(filePath) {
    try {
        const content = readFileSync(filePath, "utf-8");
        const match = content.match(/^#\s+(.+)$/m);
        return match ? match[1].trim() : filePath;
    } catch {
        return filePath;
    }
}

function isCategoryHeader(item) {
    return item.name && !item.href && (!item.items || item.items.length === 0);
}

function convertItem(item, baseUrl, tocDir) {
    const entry = {};

    if (item.name) {
        // Simplify nested names: "MaterialExtract.UnpackInfo" to "UnpackInfo"
        const dotIndex = item.name.lastIndexOf(".");
        entry.text =
            dotIndex >= 0 ? item.name.substring(dotIndex + 1) : item.name;
    }

    if (item.href) {
        if (
            item.href.startsWith("http://") ||
            item.href.startsWith("https://")
        ) {
            entry.link = item.href;
        } else if (item.href.endsWith(".md")) {
            if (item.href.startsWith("../")) {
                // Resolve relative paths against the base
                entry.link =
                    "/" +
                    item.href.replace(/^(\.\.\/)+/, "").replace(/\.md$/, "");
            } else {
                entry.link = baseUrl + item.href.replace(/\.md$/, "");
            }

            // If no name specified, read the H1 from the markdown file
            if (!entry.text) {
                entry.text = readTitleFromMarkdown(resolve(tocDir, item.href));
            }
        }
    }

    if (item.items && item.items.length > 0) {
        const children = convertItems(item.items, baseUrl, tocDir);
        if (children.length > 0) {
            entry.items = children;
            entry.collapsed = true;
        }
    }

    return entry;
}

function convertItems(items, baseUrl, tocDir) {
    const result = [];
    let currentGroup = null;

    for (const item of items) {
        // Category headers (name only, no href/items) start a new group
        if (isCategoryHeader(item)) {
            currentGroup = { text: item.name, items: [], collapsed: false };
            result.push(currentGroup);
            continue;
        }

        const entry = convertItem(item, baseUrl, tocDir);

        // Skip entries without links or children
        if (!entry.link && !entry.items) continue;

        if (currentGroup) {
            currentGroup.items.push(entry);
        } else {
            result.push(entry);
        }
    }

    // Remove empty groups
    return result.filter(
        (entry) => !entry.collapsed || entry.items?.length > 0 || entry.link,
    );
}
