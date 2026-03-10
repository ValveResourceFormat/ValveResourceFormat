# Help Write Guides

Our documentation is still growing, and we'd love your help! Whether you've figured out a workflow that others might find useful, or you want to document something you've learned about Source 2 files, contributing a guide is a great way to give back to the community.

## How to contribute

### Quick way: Edit on GitHub

1. Go to the [docs/guides](https://github.com/ValveResourceFormat/ValveResourceFormat/tree/master/docs/guides) directory on GitHub
2. Click **Add file** > **Create new file**
3. Name your file with a descriptive name using dashes, ending in `.md` (e.g. `exporting-models.md`)
4. Write your guide using Markdown
5. Click **Propose new file** at the bottom of the page to submit a pull request

::: info
You need to be logged in to your GitHub account.
:::

### Using a git fork

If you prefer working locally:

1. Fork the [ValveResourceFormat repository](https://github.com/ValveResourceFormat/ValveResourceFormat)
2. Create a new `.md` file in the `docs/guides/` directory
3. Write your guide
4. Submit a pull request to the `master` branch

::: tip
If you're not familiar with git on the command line, [GitHub Desktop](https://desktop.github.com/) makes forking, cloning, and submitting pull requests straightforward.
:::

## Writing your guide

Guides are written in Markdown rendered by [VitePress](https://vitepress.dev/guide/markdown). You can look at the existing guides for reference:

- [Valve Resource File](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/docs/guides/read-resource.md?plain=1) documents a file format with code examples
- [Command-line utility](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/docs/guides/command-line.md?plain=1) documents CLI usage with practical examples

### Tips

- Your file should begin with a `# Title` heading
- Show real commands, code snippets, or screenshots where possible
- Cover one topic per guide
- Use code blocks with language hints for syntax highlighting (e.g. ` ```cs ` for C#, ` ```powershell ` for shell commands)
- Headings at levels `##` and `###` appear in the "On this page" outline

### VitePress features

VitePress supports some useful features beyond standard Markdown:

**Custom containers** for callouts:

```md
::: tip
Helpful tip here.
:::

::: warning
Watch out for this.
:::

::: info
Additional context.
:::
```

**Code block line highlighting:**

````md
```cs{2}
var resource = new Resource();
resource.Read(stream); // highlighted line
```
````

### Images

If your guide benefits from screenshots or diagrams, place image files in the `docs/guides/images/` directory and reference them using an absolute path:

```md
![Description of the image](/images/my-screenshot.png)
```

Keep images concise and relevant. Crop to the area of interest and avoid including unnecessary UI chrome. Use PNG for screenshots and SVG for diagrams where possible.

::: tip
Use [TinyPNG](https://tinypng.com/) to optimize your images before committing them.
:::

See the [VitePress Markdown docs](https://vitepress.dev/guide/markdown) for more.

## Previewing locally

You can preview the docs site on your machine before submitting. You will need [Node.js](https://nodejs.org/) installed.

From the `docs/` directory, install dependencies and start the dev server:

```sh
npm install
npm run dev
```

This starts a local server with hot reload, so you can see your changes in real time as you edit.

## After you submit

A maintainer will review your pull request and may suggest changes. Once approved, your guide will be automatically published to the documentation site.

## Need help?

If you have questions or want to discuss a guide idea before writing, join our Discord.
