import { readdirSync, readFileSync, writeFileSync } from "fs";
import { resolve } from "path";
import { escapeHtml } from "./html.js";

// DocFX's markdown output leaves <see cref> as unresolved <xref> tags, which render as nothing,
// and uses the URL as the text of <seealso href> links. This rewrites both in the generated pages.
// It also reshapes the pages for VitePress: heading anchors become {#id} suffixes so the outline
// and search see clean text, enums and namespace listings become tables, and enum fields get
// anchors so references can link to them. Every step leaves already processed pages unchanged.

const apiDir = resolve(import.meta.dirname, "../api");
const learnUrl = "https://learn.microsoft.com/dotnet/api/";

const keywordTypes = new Map([
    ["System.Boolean", "bool"],
    ["System.Byte", "byte"],
    ["System.SByte", "sbyte"],
    ["System.Char", "char"],
    ["System.Int16", "short"],
    ["System.UInt16", "ushort"],
    ["System.Int32", "int"],
    ["System.UInt32", "uint"],
    ["System.Int64", "long"],
    ["System.UInt64", "ulong"],
    ["System.Single", "float"],
    ["System.Double", "double"],
    ["System.Decimal", "decimal"],
    ["System.String", "string"],
    ["System.Object", "object"],
    ["System.Void", "void"],
]);

// Bit width of each enum underlying type, used to print flags as hex
const enumBitWidths = new Map([
    ["byte", 8],
    ["sbyte", 8],
    ["short", 16],
    ["ushort", 16],
    ["int", 32],
    ["uint", 32],
    ["long", 64],
    ["ulong", 64],
]);

const pages = new Map();
const typePages = new Map();
const namespacePages = new Map();
const anchors = new Set();
const headingTexts = new Map();
const learnTypes = new Set();
let xrefCount = 0;

function toLearnPath(name) {
    return name.toLowerCase().replaceAll("`", "-");
}

const keywordLearnPaths = new Set();
for (const uid of keywordTypes.keys()) {
    keywordLearnPaths.add(toLearnPath(uid));
}

function unescapeMarkdown(text) {
    return text.replaceAll("\\", "");
}

function escapeCell(text) {
    return text.replace(/(?<!\\)\|/g, "\\|");
}

// Joins markdown paragraphs into one table cell
function toCell(paragraphs) {
    return escapeCell(paragraphs.join("<br>"));
}

// Splits lines into paragraphs at blank lines, keeping line breaks within a paragraph as spaces
function splitParagraphs(lines) {
    const paragraphs = [];
    let current = [];

    for (const line of lines) {
        const trimmed = line.trim();

        if (trimmed === "") {
            if (current.length > 0) {
                paragraphs.push(current.join(" "));
                current = [];
            }
            continue;
        }

        // A heading inside a description, such as Remarks, becomes a bold lead-in
        const heading = trimmed.match(/^#{1,6} (.+)$/);
        if (heading) {
            if (current.length > 0) {
                paragraphs.push(current.join(" "));
                current = [];
            }
            paragraphs.push(`**${heading[1]}**`);
            continue;
        }

        current.push(trimmed);
    }

    if (current.length > 0) {
        paragraphs.push(current.join(" "));
    }

    return paragraphs;
}

function toHex(value, bits) {
    if (!/^-?\d+$/.test(value)) {
        return "";
    }

    const unsigned = BigInt.asUintN(bits, BigInt(value));
    const digits = bits / 4;
    return `\`0x${unsigned.toString(16).toUpperCase().padStart(digits, "0")}\``;
}

// Returns the index of the next heading of level 3 or above after the heading at start
function findSectionEnd(lines, start) {
    let end = start + 1;
    while (end < lines.length && !/^#{1,3} /.test(lines[end])) {
        end++;
    }
    return end;
}

function buildEnumTable(fieldLines, pageUid, isFlags, bits) {
    const fields = [];
    let current = null;

    for (const line of fieldLines) {
        const field = line.match(/^`(\w+) = ([^`\n]*)`\s*$/);
        if (field) {
            current = { name: field[1], value: field[2], body: [] };
            fields.push(current);
        } else if (current) {
            current.body.push(line);
        }
    }

    const header = isFlags
        ? ["| Name | Value | Hex | Description |", "| --- | --- | --- | --- |"]
        : ["| Name | Value | Description |", "| --- | --- | --- |"];
    const rows = [];

    for (const field of fields) {
        const description = toCell(splitParagraphs(field.body));
        const name = `<a id="${pageUid}_${field.name}"></a>\`${field.name}\``;

        if (isFlags) {
            const hex = toHex(field.value, bits);
            rows.push(`| ${name} | ${field.value} | ${hex} | ${description} |`);
        } else {
            rows.push(`| ${name} | ${field.value} | ${description} |`);
        }
    }

    return [...header, ...rows];
}

function convertEnumFields(content, pageUid) {
    const lines = content.split("\n");
    const start = lines.indexOf("## Fields");
    if (start < 0) {
        return content;
    }

    const end = findSectionEnd(lines, start);
    const body = lines.slice(start + 1, end);
    if (!body.some((line) => /^`\w+ = /.test(line))) {
        return content;
    }

    const signature = content.match(
        /^(?:public|protected)[\w ]* enum [^\n:]+(?:: (\w+))?$/m,
    );
    const underlying = signature?.[1] ?? "int";
    const bits = enumBitWidths.get(underlying) ?? 32;
    const isFlags = /^\[Flags\]$/m.test(content);

    const table = buildEnumTable(body, pageUid, isFlags, bits);
    lines.splice(start + 1, end - start - 1, "", ...table, "");

    return lines.join("\n");
}

function compareNames(a, b) {
    const left = a.toLowerCase().split(".");
    const right = b.toLowerCase().split(".");
    const count = Math.min(left.length, right.length);

    for (let i = 0; i < count; i++) {
        if (left[i] < right[i]) {
            return -1;
        }
        if (left[i] > right[i]) {
            return 1;
        }
    }

    return left.length - right.length;
}

function isListingEntry(line) {
    return /^ \[[^\]]+\]\([^)]+\)\s*$/.test(line);
}

function buildNamespaceListing(sectionLines) {
    const entries = [];
    let current = null;

    for (const line of sectionLines) {
        const link = line.match(/^ \[([^\]]+)\]\(([^)]+)\)\s*$/);
        if (link) {
            current = { name: link[1], href: link[2], body: [] };
            entries.push(current);
        } else if (current) {
            current.body.push(line);
        }
    }

    entries.sort((a, b) => compareNames(a.name, b.name));

    let hasDescriptions = false;
    for (const entry of entries) {
        // A listing only needs the opening paragraph, the type's page has the rest
        const paragraphs = splitParagraphs(entry.body);
        entry.description = escapeCell(paragraphs[0] ?? "");

        if (entry.description !== "") {
            hasDescriptions = true;
        }
    }

    // Child namespaces have no summaries, so a table would only add an empty column
    if (!hasDescriptions) {
        const items = [];
        for (const entry of entries) {
            items.push(`- [${entry.name}](${entry.href})`);
        }
        return items;
    }

    const rows = ["| Name | Description |", "| --- | --- |"];
    for (const entry of entries) {
        // Lets long nested type names wrap at the dot instead of widening the column
        const name = entry.name.replaceAll(".", ".<wbr>");
        rows.push(`| [${name}](${entry.href}) | ${entry.description} |`);
    }

    return rows;
}

function convertNamespaceListings(content) {
    const lines = content.split("\n");
    const output = [];
    let i = 0;

    while (i < lines.length) {
        const line = lines[i];
        output.push(line);

        if (
            !/^### (Namespaces|Classes|Structs|Enums|Interfaces|Delegates)$/.test(
                line,
            )
        ) {
            i++;
            continue;
        }

        const end = findSectionEnd(lines, i);
        const body = lines.slice(i + 1, end);
        if (body.some(isListingEntry)) {
            output.push("", ...buildNamespaceListing(body), "");
        } else {
            output.push(...body);
        }
        i = end;
    }

    return output.join("\n");
}

function stripKeywordLink(link, text, learnPath) {
    if (keywordLearnPaths.has(learnPath)) {
        return text;
    }

    return link;
}

// Converts a page and registers its title so references to it can resolve
function convertPage(file, content) {
    // Keep the docfx id as an explicit heading id instead of raw HTML in the heading text
    content = content.replace(
        /^(#{1,6}) <a id="([^"]+)"><\/a> (.+)$/gm,
        "$1 $3 {#$2}",
    );

    content = content.replace(
        /\[([^\]]*)\]\(https:\/\/learn\.microsoft\.com\/dotnet\/api\/([\w.]+)\)/g,
        stripKeywordLink,
    );

    const title = content.match(/^# (\w+) (.+) \{#([^}]+)\}$/m);
    if (!title) {
        console.warn(
            `No title found in ${file}, references to it will not resolve.`,
        );
        return content;
    }

    const [, kind, text, anchor] = title;
    const page = {
        href: file.replace(/\.md$/, ".html"),
        title: unescapeMarkdown(text),
    };

    if (kind === "Namespace") {
        namespacePages.set(anchor, page);
        return convertNamespaceListings(content);
    }

    typePages.set(anchor, page);

    if (kind === "Enum") {
        return convertEnumFields(content, anchor);
    }

    return content;
}

function indexPage(content) {
    for (const match of content.matchAll(/<a id="([^"]+)"><\/a>/g)) {
        anchors.add(match[1]);
    }

    for (const match of content.matchAll(/^#{1,6} (.+) \{#([^}]+)\}$/gm)) {
        if (!anchors.has(match[2])) {
            anchors.add(match[2]);
            headingTexts.set(match[2], unescapeMarkdown(match[1]));
        }
    }

    for (const match of content.matchAll(
        /\(https:\/\/learn\.microsoft\.com\/dotnet\/api\/([^)#]+)\)/g,
    )) {
        learnTypes.add(unescapeMarkdown(match[1]));
    }
}

function toAnchor(uid) {
    return uid.replace(/[^A-Za-z0-9]/g, "_");
}

function stripParameters(uid) {
    const parenIndex = uid.indexOf("(");
    return parenIndex >= 0 ? uid.substring(0, parenIndex) : uid;
}

function memberName(uid) {
    const name = stripParameters(uid);
    return name.substring(name.lastIndexOf(".") + 1).replace(/`+\d+$/, "");
}

function parentUid(uid) {
    const name = stripParameters(uid);
    return name.substring(0, name.lastIndexOf("."));
}

function typeDisplayName(uid) {
    return keywordTypes.get(uid) ?? memberName(uid);
}

function resolveInternal(uid) {
    const anchor = toAnchor(uid);
    const page = typePages.get(anchor) ?? namespacePages.get(anchor);
    if (page) {
        return { href: page.href, text: page.title };
    }

    // Members without a section of their own, such as non-public ones, link to their type's page.
    // A type without a page of its own is not linked to its namespace's page.
    const type = typePages.get(toAnchor(parentUid(uid)));
    if (!type) {
        return null;
    }

    let href = type.href;
    if (anchors.has(anchor)) {
        href = `${type.href}#${anchor}`;
    }

    // Methods use their heading, which lists parameter types and tells overloads apart.
    // A constructor's heading already starts with the type name.
    const member = memberName(uid);
    const heading = headingTexts.get(anchor);
    let text = `${type.title}.${member}`;
    if (member === "#ctor") {
        text = heading ?? type.title;
    } else if (heading?.includes("(")) {
        text = `${type.title}.${heading}`;
    }

    return { href, text };
}

function resolveExternal(uid) {
    const name = stripParameters(uid);
    const parent = parentUid(uid);
    const hasParameters = uid.includes("(");
    const isKnownTypeMember =
        keywordTypes.has(parent) || learnTypes.has(toLearnPath(parent));

    // Our own namespaces are all known, so anything else under one of them is a member of an
    // undocumented type
    const isUndocumentedMember =
        uid.startsWith("ValveResourceFormat.") &&
        !namespacePages.has(toAnchor(parent));

    let text = typeDisplayName(name);
    if (hasParameters || isKnownTypeMember || isUndocumentedMember) {
        text = `${typeDisplayName(parent)}.${memberName(uid)}`;
    }

    // Keyword types and their members are self-explanatory, so they are not linked
    const isKeyword = keywordTypes.has(name) || keywordTypes.has(parent);
    if (!uid.startsWith("System.") || isKeyword) {
        return { href: null, text };
    }

    return { href: learnUrl + toLearnPath(name), text };
}

function renderXref(_, href, innerText) {
    xrefCount++;

    const uid = decodeURIComponent(href);
    const target = resolveInternal(uid) ?? resolveExternal(uid);
    const text = innerText || escapeHtml(target.text);

    if (!target.href) {
        return `<code>${text}</code>`;
    }

    return `<a href="${target.href}">${text}</a>`;
}

function renderUrlLink(_, url) {
    const path = url.replace(/\/+$/, "");
    return `[${path.substring(path.lastIndexOf("/") + 1)}](${url})`;
}

// VitePress writes the meta description into a double quoted attribute without escaping it, so
// double quotes become single quotes
function toPlainText(markdown) {
    const text = unescapeMarkdown(
        markdown
            .replace(/<[^>]+>/g, "")
            .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
            .replace(/\*\*|`/g, ""),
    );

    return text
        .replaceAll("&lt;", "<")
        .replaceAll("&gt;", ">")
        .replaceAll("&amp;", "&")
        .replaceAll('"', "'")
        .replace(/\s+/g, " ")
        .trim();
}

// The meta description of a page: a namespace's name, or the opening paragraph of a type's
// summary, which follows the assembly line. Pages without a summary get none.
function describePage(content) {
    const namespace = content.match(/^# Namespace (.+) \{#/m);
    if (namespace) {
        return `API reference for the ${unescapeMarkdown(namespace[1])} namespace.`;
    }

    const lines = content.split("\n");
    let i = lines.findIndex((line) => line.startsWith("Assembly: "));
    if (i < 0) {
        return null;
    }

    i++;
    while (i < lines.length && lines[i].trim() === "") {
        i++;
    }

    const paragraph = [];
    while (i < lines.length && lines[i].trim() !== "") {
        paragraph.push(lines[i]);
        i++;
    }

    if (paragraph.length === 0 || paragraph[0].startsWith("```")) {
        return null;
    }

    return toPlainText(paragraph.join(" "));
}

// Docfx pages start with their title, so a page with frontmatter has already been described
function addDescription(content) {
    if (content.startsWith("---\n")) {
        return content;
    }

    const description = describePage(content);
    if (!description) {
        return content;
    }

    return `---\ndescription: ${JSON.stringify(description)}\n---\n\n${content}`;
}

for (const file of readdirSync(apiDir)) {
    if (!file.endsWith(".md")) {
        continue;
    }

    const content = convertPage(
        file,
        readFileSync(resolve(apiDir, file), "utf-8"),
    );
    indexPage(content);
    pages.set(file, content);
}

for (const [file, content] of pages) {
    const updated = addDescription(
        content
            .replace(
                /<xref href="([^"]*)"[^>]*>([\s\S]*?)<\/xref>/g,
                renderXref,
            )
            .replace(/\[(https?:\/\/[^\]\s]+)\]\(\1\)/g, renderUrlLink),
    );

    if (updated.includes("<xref")) {
        console.warn(`Unresolved <xref> left in ${file}.`);
    }

    writeFileSync(resolve(apiDir, file), updated);
}

console.log(`Resolved ${xrefCount} cross references.`);
