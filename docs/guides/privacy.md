---
description: Source 2 Viewer collects no personal data; this page explains the update check, update downloads and the websites involved.
---

# Privacy

Source 2 Viewer does not collect or share any personal data. There is no telemetry, no crash
reporting, no analytics, and no account. Files you open never leave your computer, and your
Steam library is only read locally to list installed games. The command-line tool makes no
network requests at all.

## Update Check

Source 2 Viewer periodically asks our update server whether a newer version exists. The
request contains only the app version, and nothing about you, your computer, or the files
you open. We do not log who asked, though the hosting provider sees your IP address and keeps
its own standard logs.

Automatic checks are enabled by default and can be turned off in the About dialog. Opening
the About dialog also checks.

## Downloading an Update

When you choose to download an update, the file is fetched from GitHub, and it is verified to
be a genuine build using the GitHub API and the public [Sigstore](https://sigstore.dev)
service. These services see your IP address as part of serving the request, as with any
download.

## Websites

[s2v.app](https://s2v.app) and the update server are hosted by third parties, currently
GitHub Pages and Cloudflare, whose standard server logs apply. See the
[GitHub privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)
and the [Cloudflare privacy policy](https://www.cloudflare.com/privacypolicy/). The front
page loads release and contributor information from GitHub in your browser.

We do not run analytics or set cookies ourselves, though Cloudflare may set cookies as part
of its bot protection. The site remembers small things in your browser, such as your theme.

## Local Data

Settings, including recent files and bookmarks, are stored in
`%LocalAppData%/Source2Viewer/settings.vdf` and never uploaded.
