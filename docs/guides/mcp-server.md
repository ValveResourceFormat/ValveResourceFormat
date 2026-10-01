# MCP Automation Server

Source 2 Viewer has an opt-in loopback server that lets an agent or script drive the real viewer window over the Model Context Protocol: open files, move the camera, toggle layers and render modes, drive the sidebar controls, inspect entities, models and particle systems, pick, trace, hide nodes, pause and step simulation time, read the log, render stats and memory use, and take screenshots.

It exists for developing and testing the viewer itself. The tools can open anything the user could open through the UI, and there is no sandbox.

## Starting the Server

The server only exists in Debug builds, so build and run the GUI from source:

```powershell
dotnet run --project GUI -- --mcp
```

- `--mcp` listens on the default port `13338`; `--mcp=<port>` picks another.
- The endpoint is `http://127.0.0.1:<port>/mcp`. It listens on the loopback address only.
- A Release build given `--mcp` prints an error and exits.
- If the port is already in use, the viewer prints the error and exits.

## Connecting

The server speaks MCP `2026-07-28`: stateless JSON-RPC 2.0 over `POST /mcp`, with no `initialize` handshake. Every request carries the protocol version and client capabilities in `params._meta`, and mirrors the protocol version, method and tool name in headers. Clients built around the older handshake do not fit, so call it directly:

```sh
curl http://127.0.0.1:13338/mcp \
  -H "Content-Type: application/json" \
  -H "MCP-Protocol-Version: 2026-07-28" \
  -H "Mcp-Method: tools/call" \
  -H "Mcp-Name: get_status" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "get_status",
      "arguments": {},
      "_meta": {
        "io.modelcontextprotocol/protocolVersion": "2026-07-28",
        "io.modelcontextprotocol/clientCapabilities": {}
      }
    }
  }'
```

`tools/list` returns every tool with its description and argument schema, and `server/discover` returns the server's instructions. Both take the same `_meta` and headers, without `Mcp-Name`. A failing tool returns a normal result with `isError: true` and a plain text explanation.

## Tools

| Tool                                   | Description                                                                                                 |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| **Application and tabs**               |                                                                                                             |
| `get_status`                           | Process id, version, uptime, the active tab, whether the simulation is paused, and any unhandled exception. |
| `quit`                                 | Close the viewer. Needed before rebuilding, because a running instance holds the build output open.         |
| `list_tabs`                            | Open tabs with their id, title, file, viewer kind, and any error the viewer showed instead.                 |
| `select_tab`, `close_tab`              | Make a tab active, or close it. Only the active tab renders.                                                |
| `open_file`                            | Open a file and wait until its tab has finished loading. Fails with the exception when the viewer does.     |
| `reload_tab`                           | Reopen a tab from its file, as Ctrl+R does, and wait for it to load. Returns the new tab id.                |
| `get_info`                             | Describe any tab, including which tools work on it and the spawn groups a map loaded.                       |
| `list_package`                         | List or search the files in a package tab or a .vpk on disk, paged, with sizes, types and vpk: links.       |
| **Camera and view**                    |                                                                                                             |
| `get_camera`, `set_camera`             | Read or move the camera of a 3D tab. Reading also gives the view direction and render area size.            |
| `frame`                                | Frame an entity, a node, a box or the whole scene, landing there at once.                                   |
| `screenshot`                           | Capture what a tab renders: a 3D view, texture, image or graph. Model tabs can capture transparently.       |
| `set_viewport`                         | Render a 3D tab at an exact pixel size, so screenshots from two builds can be compared.                     |
| `list_render_modes`, `set_render_mode` | List or switch the debug render modes of a 3D tab.                                                          |
| `list_controls`                        | The checkboxes, dropdowns, checked lists, sliders and number inputs of a tab's sidebar, with their state.   |
| `set_checkbox`                         | Toggle a sidebar checkbox, such as fog or PVS culling, or one item of a checked list, such as a mesh group. |
| `set_dropdown`                         | Select a dropdown item, such as an animation, LOD, map camera or texture mip.                               |
| `set_value`                            | Move a slider, such as exposure or the animation frame, or set a number input, such as a depth of field.    |
| `reload_shaders`                       | Recompile shaders from the source tree without restarting the viewer.                                       |
| `get_render_stats`                     | Draw counts and renderer metrics of a fresh frame.                                                          |
| **Maps and entities**                  |                                                                                                             |
| `list_layers`                          | World layers and physics groups of a map, each with whether it is drawn.                                    |
| `set_layer`, `set_physics_group`       | Show or hide one world layer or physics group.                                                              |
| `find_entities`, `get_entity`          | Search the entities of a map and its spawn groups by name, keyvalue or trigger, or read one.                |
| `select_entity`                        | Select an entity and move the camera to it.                                                                 |
| `pick`, `clear_selection`              | Identify the node, mesh and entity under a pixel, select, add or open it, or drop the selection.            |
| `trace`                                | Trace a ray or box through a map's collision, or read what a pixel shows and how far away it is.            |
| `set_hidden`, `list_hidden`            | Hide or show nodes the way the Delete key does, or list the nodes hidden that way.                          |
| `get_model_info`                       | Bones, bounds, attachments and other facts about a model that the sidebar does not show.                    |
| **Simulation and particles**           |                                                                                                             |
| `pause`, `resume`                      | Freeze or resume the simulation of every tab. Frames still render while paused.                             |
| `step`                                 | Advance the simulation by an exact number of seconds in fixed frames, then stay paused.                     |
| `get_particles`                        | Particle systems with their age, particle count, control points, bounds and unsupported renderers.          |
| `particle_playback`                    | Restart, pause, resume or play the endcap of a particle system tab through its sidebar buttons.             |
| `list_particle_functions`              | The functions of a particle system tab marked supported, unsupported or removed, and its children.          |
| **Diagnostics**                        |                                                                                                             |
| `get_log`, `clear_log`                 | Read or clear the viewer's console. Pass the returned cursor back to read only newer lines.                 |
| `get_memory`                           | Process and managed heap memory, optionally after a full garbage collection.                                |

Tools that act on a tab take an optional `tab` id from `open_file` or `list_tabs` and use the active tab when it is omitted.

## Behavior

- Files inside a package use `vpk:package.vpk:inner/path`, the same links the command line accepts (see [Getting Started](./getting-started.md#opening-vpk-links)).
- Calls run one at a time, because the viewer state they drive is global. `get_status`, `get_log`, `list_tabs` and `resume` are the exception: they answer while another call is running, so `resume` can abandon a running `step`. A `notifications/cancelled` naming a running request cancels it.
- The viewer keeps settings changes in memory only and writes nothing to its settings file: no recent files, window placement, field of view or saved cameras.
- Replies are ASCII. Other characters in paths and log lines come back as JSON unicode escapes.
- `screenshot` writes only to an absolute `.png` path in a folder that exists, and checks that before capturing.
- Calls that change the view draw a frame before answering, so the window shows the result even while it is in the background. Screenshots work in the background too, but not while the window is minimized.
- For repeatable screenshots, `pause` and then `step` exact amounts of time. Particles do not emit while paused, so step after loading a map to see them.
- Tools that need a 3D scene only work on 3D tabs (models, maps, materials, particle systems). Texture, image and graph tabs take `screenshot` and the sidebar tools.
- A tool that acts on a tab that is still loading waits for the load to finish.
- An unhandled exception is reported through `get_status` and the failing tool instead of opening the error dialog.
- Scene nodes have ids like `0:123`: the index of their scene, 0 for the map's own and then one per spawn group such as the 3D sky, and the node's id in it. Entities of the map and its 3D sky keep their integer ids; entities of other spawn groups, such as stages a map loads while it plays, get ids from 1000000 up.
- Pixel coordinates for `pick` and `trace` run from the top left of the render area, whose size `get_camera` returns. A pixel showing no node answers with background `sky` or `nothing`; one outside the render area is an error.
- `set_hidden` drives the same selection and Delete key path as the viewer, so it drops the selection. Turning any world layer on or off applies the layers to every node again, which shows every node hidden this way or with Delete.

## Example Session

Each of these is the `arguments` of a `tools/call` request.

```json
{ "path": "vpk:C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo/maps/de_dust2.vpk:maps/de_dust2.vmap_c" }
```

`open_file` returns the new tab, for example `{"tab": 1, "title": "de_dust2.vmap_c", ...}`. Then move the camera with `set_camera`, and capture it with `screenshot`:

```json
{ "tab": 1, "position": [0, 0, 300], "angles": [15, 45, 0] }
```

```json
{ "tab": 1, "path": "C:/temp/dust2.png" }
```
