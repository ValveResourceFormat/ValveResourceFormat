# MCP Automation Server

Source 2 Viewer has an opt-in loopback server that lets an agent drive the real viewer window over the Model Context Protocol: open files, move the camera, drive the sidebar controls, inspect entities, models and particle systems, pick, trace, pause and step simulation time, read the log and render stats, and take screenshots.

It exists for developing and testing the viewer itself. The tools can open anything the user could open through the UI, and there is no sandbox.

## Starting the Server

The server only exists in Debug builds, so build and run the GUI from source:

```powershell
dotnet run --project GUI -- --mcp
```

`--mcp` listens on `http://127.0.0.1:13338/mcp`, and `--mcp=<port>` picks another port.

## Connecting an Agent

Add the endpoint to your agent as an HTTP MCP server, for example in Claude Code:

```sh
claude mcp add --transport http source2viewer http://127.0.0.1:13338/mcp
```

Once the viewer is running with `--mcp`, the agent sees the tools and their descriptions, and drives the viewer through them.

While an agent drives the viewer, settings changes stay in memory and are not saved, and unhandled exceptions are reported to the agent instead of opening the error dialog.
