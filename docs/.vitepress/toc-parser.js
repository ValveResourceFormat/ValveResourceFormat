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

function convertItems(items, baseUrl, tocDir) {
    const result = [];

    for (const item of items) {
        const entry = {};

        if (item.name) {
            entry.text = item.name;
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
                        item.href
                            .replace(/^(\.\.\/)+/, "")
                            .replace(/\.md$/, "");
                } else {
                    entry.link = baseUrl + item.href.replace(/\.md$/, "");
                }

                // If no name specified, read the H1 from the markdown file
                if (!entry.text) {
                    entry.text = readTitleFromMarkdown(
                        resolve(tocDir, item.href),
                    );
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

        // Skip category headers without links (like "Classes", "Structs", "Enums")
        if (!entry.link && !entry.items) continue;

        result.push(entry);
    }

    return result;
}
