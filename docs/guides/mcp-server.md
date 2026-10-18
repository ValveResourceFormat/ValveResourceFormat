# MCP Automation Server

Source 2 Viewer has an opt-in loopback server that lets an agent drive the real viewer window over the Model Context Protocol. The agent can open files, move the camera, change sidebar controls, and inspect entities, models and particle systems. It can also pick, trace, pause and step simulation time, read the log and render stats, and take screenshots.

The server is for development and testing of the viewer. The tools can open anything the user can open in the UI, and there is no sandbox.

## Starting the Server

Only Debug builds have the server, so build and run the GUI from source:

```powershell
dotnet run --project GUI -- --mcp
```

`--mcp` listens on `http://127.0.0.1:13338/mcp`. `--mcp=<port>` selects a different port, so several viewers can run at the same time.

The server speaks only MCP revision `2026-07-28`.

## Connecting an Agent

Add the endpoint to your agent as an HTTP MCP server. For example, in Claude Code:

```sh
claude mcp add --transport http source2viewer http://127.0.0.1:13338/mcp
```

When the viewer runs with `--mcp`, the agent sees the tools and their descriptions, and uses them to drive the viewer.

While an agent drives the viewer, settings changes stay in memory and the viewer does not save them. The viewer reports errors to the agent instead of opening the error dialog.
