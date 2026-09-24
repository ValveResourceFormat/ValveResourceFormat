# MCP automation server

Source 2 Viewer has an opt-in loopback server that lets an agent or script drive the real viewer window over the Model Context Protocol: open files, move the camera, toggle layers and render modes, inspect entities and particle systems, pick, pause and step simulation time, read the log, render stats and memory use, and screenshot what it draws.

It exists for developing and testing the viewer itself. The tools can open anything the user could open through the UI, and there is deliberately no sandbox.

## Availability

The server only exists in Debug builds. `--mcp` is stripped out of the command line before anything else reads it, so in a Release build it prints `--mcp is only available in debug builds.` and exits with code 1 instead of doing anything.

Build and run the GUI from source to use it:

```powershell
dotnet run --project GUI -- --mcp
```

## Starting the server

Pass `--mcp` for the default port, or `--mcp=<port>` for a specific one (1-65535). The switch is consumed before the rest of the command line is interpreted, so it is never forwarded to an already running instance or mistaken for a file to open.

- Default port: `13338`
- Endpoint: `http://127.0.0.1:<port>/mcp`
- Listens on `127.0.0.1` only; nothing outside the machine can reach it.
- Not sandboxed: a tool can open, and screenshot, anything the user could open through the UI.
- If the port is already in use, the viewer prints `Could not listen on port <port>: <message>` and exits.

## Protocol

The server speaks MCP `2026-07-28`. That revision has no `initialize` handshake: every request carries its own protocol version, so the server keeps nothing between calls. It is stateless JSON-RPC 2.0 over one HTTP endpoint, `POST /mcp`. Any other path answers `404`; any method other than `POST` answers `405`.

Every request must:

- Have `Content-Type: application/json` (otherwise `415`), and be at most 4 MiB (otherwise `413`).
- Carry a `_meta` object in `params` with two `io.modelcontextprotocol/`-prefixed fields: `protocolVersion` and `clientCapabilities`. Missing either is a `-32602` error; a `protocolVersion` other than `2026-07-28` is a `-32022` "Unsupported protocol version" error that lists what is supported.
- Repeat part of that in headers, which the server checks against the body so that a proxy routing on the header and the server acting on the body can never disagree:
    - `MCP-Protocol-Version` must equal `params._meta["io.modelcontextprotocol/protocolVersion"]`.
    - `Mcp-Method` must equal the JSON-RPC `method`.
    - `Mcp-Name` is required on `tools/call` only, and must equal `params.name`. A name that is not header-safe is wrapped as `=?base64?<base64>?=`, which the server decodes before comparing.
- If it carries an `Origin` header, that origin must be loopback (this stops a browser page on another site from driving the viewer); anything else is `403`.

A request with no `id` is a notification and gets a bare `202` with no body. Batching is not supported: the body must be a single JSON object, not an array. A body that fails to parse is `400` with JSON-RPC error `-32700`; one that is not a single object, or has no `method`, is `-32600`.

The server answers three methods:

| Method | Answers with |
| --- | --- |
| `server/discover` | Supported versions, capabilities, and the instructions text below. |
| `tools/list` | The tool table: name, description, input schema. |
| `tools/call` | Runs the named tool and returns its result. |

`initialize` always fails with a `-32601` error naming the protocol version, since this revision has no handshake to answer it with. Any other method is `-32601` "Method not found". Both `server/discover` and `tools/list` are stamped with `ttlMs: 3600000` and `cacheScope: "public"`, since nothing about them changes while the process runs, so a client may cache them for an hour.

A tool failure is not a JSON-RPC error. `tools/call` always returns a normal JSON-RPC result with `isError: true` and a plain text explanation; JSON-RPC errors are reserved for protocol problems such as an unknown method or an unknown tool name. Every result, successful or not, also carries `resultType: "complete"` and `_meta["io.modelcontextprotocol/serverInfo"]` with the server name and version.

The `server/discover` instructions:

> Drives the Source 2 Viewer window: open files, move the camera, toggle layers and render modes, inspect entities, pick, read the log and render stats, and screenshot what it draws.
> Successful calls answer with compact JSON; failures answer with plain text and isError. Optional fields are left out when they would be false, null or empty.
> Positions are [x, y, z] world units and angles are [pitch, yaw, roll] degrees with pitch positive downwards, rounded to two decimals.
> Calls that change the view draw a frame before answering, so the window shows the result even in the background. For repeatable screenshots, pause and then step exact amounts of time.
> Tools that take 'tab' use the active tab when it is omitted. Pass the id from open_file or list_tabs, because a failed load or the user can change which tab is active. A tool that acts on a tab that is still loading waits for the load to finish.
> A 3D tab is a model, map, material, particle system or other scene; texture, image and graph tabs render too but have no scene. get_info on a tab lists which of the tools that act on a tab work on it.

### Connecting

This revision has no `initialize` handshake and requires the headers above on every call, so it does not fit a client built around the standard streamable HTTP handshake. Call it directly. A `tools/call` request for `get_status` looks like this:

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

`server/discover` and `tools/list` take the same `_meta` and headers, without `Mcp-Name`.

## Tools

| Tool | Description |
| --- | --- |
| **Application and tabs** | |
| `get_status` | Server and application status: process id, version, uptime, the active tab and whether the simulation is paused. |
| `quit` | Close the viewer. Required before rebuilding, because a running instance holds the build output open. |
| `list_tabs` | List open tabs with their id, title, file and viewer kind, marking the active one. |
| `select_tab` | Make a tab active. Only the active tab renders. |
| `close_tab` | Close a tab. |
| **Files** | |
| `open_file` | Open a file and wait until its tab has finished loading. Accepts the same paths as the command line. A file inside a package is `vpk:outer_dir.vpk:inner/file`, so a map is `vpk:game/pak01_dir.vpk:maps/name.vmap_c`; a bare `.vpk` only opens the package browser. |
| **Camera and view** | |
| `get_camera` | Read the camera position, angles and field of view of a 3D tab. |
| `set_camera` | Move the camera of a 3D tab instantly, with no fly-in transition. Takes the same shape `get_camera` returns. |
| `screenshot` | Capture what a rendered tab shows: a 3D view, texture, image or graph. Returns a downscaled JPEG to look at; pass `path` to also get the full resolution PNG on disk. Selects the tab and renders a fresh frame first, so it works while the window is in the background. A 3D tab captures the frame as the window shows it; a texture or image tab captures the whole image at its selected mip, and a graph tab the whole graph, regardless of pan and zoom. |
| `clear_selection` | Drop the current selection of a 3D tab. The outline of a selected node, and the debug geometry of a selected light probe volume or envmap, stay in every screenshot until this is called. |
| `set_viewport` | Render at an exact pixel size regardless of the window size, so screenshots from two builds can be compared. Pass no size to go back to following the window. |
| `list_render_modes` | List the debug render modes available for a 3D tab, and which one is active. |
| `set_render_mode` | Switch the debug render mode of a 3D tab, for isolating lighting, specular, overdraw and similar. |
| `reload_shaders` | Recompile shaders from the source tree and redraw, without restarting the viewer. A compile failure comes back as the compiler's own error text. |
| `get_info` | Describe any tab: its viewer kind and file, and which of the tools that act on a tab work on it. A 3D tab adds its render mode and node counts, and a map its name, entity counts and 3D sky. |
| `get_render_stats` | Per frame draw counts and renderer metrics of a fresh frame of a 3D tab, the numbers behind the performance overlay. Counters that are zero are left out. |
| **Map layers and entities** | |
| `list_layers` | List the world layers and physics groups of a map, each with whether it is drawn. |
| `set_layer` | Show or hide one world layer. The 3D sky scene follows. |
| `set_physics_group` | Show or hide one physics group, such as the collision hulls of triggers or player clips. |
| `find_entities` | Search the entities of a map and its 3D sky. Positions are where each entity renders, so a 3D sky entity's position can be passed straight to `set_camera`. Returns `next_offset` when more matches remain. |
| `get_entity` | Everything about one entity: its keyvalues and outputs, where it renders, the class it spawned as with its live transform, and its scene nodes with their layer, physics group, materials and whether they are hidden. |
| `select_entity` | Select an entity and move the camera to it, as double clicking it in the entity list does. Like that, it turns on the layer and physics group of the entity's node when they are off, and reports which it turned on. The selection outline stays until `clear_selection`. |
| `pick` | Identify the scene node and entity under a viewport pixel of a 3D tab. Coordinates are from the top left of the render area. Only reads by default; pass `select` to select it as clicking would. |
| **Simulation and particles** | |
| `pause` | Freeze the simulation of every tab: entities, particles, animation and shader time. Frames still render, so the camera can move and screenshots stay exactly repeatable. Particles do not emit while paused, so `step` after loading a map to see them. |
| `resume` | Go back to real time simulation, abandoning a `step` that is still running. |
| `step` | Pause, then advance the simulation of a 3D tab by exactly this many seconds in fixed frames, and stay paused. Two runs stepped the same way show the same thing. Returns the scene time afterwards. |
| `get_particles` | Inspect the particle systems of a 3D tab: whether each is paused or finished, its age and live particle count including child systems, its control points, bounds, and the renderer classes it uses that are not implemented and so draw nothing. |
| **Diagnostics** | |
| `get_log` | Read the viewer's console, oldest first: shader compile errors, load failures and renderer warnings. Each line is `time level [component] message`, level being D, I, W or E. Returns a `cursor`; pass it back as `since` to read only what was logged after this call. |
| `clear_log` | Empty the viewer's console. Returns the cursor to pass to `get_log` as `since`. |
| `get_memory` | Memory use of the viewer process in MB: private bytes and working set, the managed heap (live objects, fragmentation and committed), and garbage collection counts per generation. Pass `collect` to first run a full blocking collection that also compacts the large object heap and runs finalizers, which tells a leak (the heap stays up) from memory that is merely not yet collected. Pooled buffers are only released by collections after sitting unused for 30 to 60 seconds, so a heap that stays up right after closing a large file should be measured again a minute later. |

### Arguments

Most tools take an optional `tab` (integer): the tab id from `open_file` or `list_tabs`, defaulting to the active tab. `select_tab` and `close_tab` require it instead. `get_status`, `quit`, `get_log`, `clear_log`, `get_memory`, `list_tabs`, `pause` and `resume` act process-wide and take no `tab`. `get_camera`, `list_layers`, `list_render_modes`, `get_info`, `get_render_stats` and `clear_selection` take only that shared `tab` argument. The rest also take:

**`get_log`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `since` | integer | `0` | Only lines logged after this cursor, from an earlier `get_log` or `clear_log`. |
| `level` | string (`debug`, `info`, `warn`, `error`) | `info` | Lowest level to include; `info` leaves out debug lines such as OpenGL performance notes. |
| `include` | string (regex) | none | Case insensitive regular expression; only matching lines are returned, matched against `[component] message`. |
| `exclude` | string (regex) | none | Case insensitive regular expression; matching lines are left out. |
| `dedupe` | boolean | `true` | Collapse repeats of the same line into its first occurrence with an `(xN)` count. |
| `limit` | integer | `200` | Most lines to return, keeping the newest. At most 5000. |

**`get_memory`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `collect` | boolean | `false` | Run a full compacting collection before measuring. |

**`select_tab`** / **`close_tab`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `tab` | integer | required | Tab id from `list_tabs`. |

**`open_file`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `path` | string | required | File path, or `vpk:package.vpk:inner/file` for a file inside a package. |
| `timeout_seconds` | integer | `180` | How long to wait for loading. |

**`screenshot`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `tab` | integer | active tab | Tab id from `open_file` or `list_tabs`. |
| `settle_frames` | integer | `2` | Extra frames to render before capturing, for auto exposure to settle. |
| `path` | string | none | Absolute path to write the full resolution PNG to. The inline image is a JPEG, so do not save it as `.png`. |

**`set_camera`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `tab` | integer | active tab | Tab id from `open_file` or `list_tabs`. |
| `position` | number[3] | required | World position `[x, y, z]`. |
| `angles` | number[3] | current pitch/yaw, roll `0` | `[pitch, yaw, roll]` in degrees, pitch positive downwards. |
| `look_at` | number[3] | none | World point `[x, y, z]` to face, instead of `angles`. |
| `fov` | number | left unchanged | Field of view in degrees. |

**`set_layer`** / **`set_physics_group`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `name` | string | required | Layer, or physics group, name from `list_layers`. |
| `enabled` | boolean | required | Whether it should be drawn. |

**`set_render_mode`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `name` | string | required | Render mode name from `list_render_modes`. |

**`set_viewport`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `width` | integer (1-8192) | follows window | Render width. Omit along with `height` to follow the window again. |
| `height` | integer (1-8192) | follows window | Render height. |

**`reload_shaders`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `name` | string | every shader | Only reload shaders derived from this file, for example `complex.frag.slang`. Reloading every shader is much slower. |

**`find_entities`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `classname` | string | none | Match entities whose classname contains this. |
| `targetname` | string | none | Match entities whose targetname contains this. |
| `near` | number[3] | none | Only entities within `radius` of this world point. |
| `radius` | number | `512` | Distance from `near`. |
| `sky` | boolean | both | `true` for only 3D sky entities, `false` for none. |
| `offset` | integer | `0` | Matches to skip, from an earlier `next_offset`. |
| `limit` | integer | `50` | Most matches to return. At most 1000. |

**`get_entity`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `id` | integer | required | Entity id from `find_entities` or `pick`. |

**`select_entity`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `id` | integer | required | Entity id from `find_entities` or `pick`. |
| `instant` | boolean | `true` | Skip the fly-in and jump straight there. |

**`pick`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `x` | integer | required | Pixel X in the render area. |
| `y` | integer | required | Pixel Y in the render area. |
| `select` | boolean | `false` | Also select what was hit, drawing its outline until `clear_selection`. |

**`step`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `seconds` | number | required | Seconds to simulate, at most 60. |
| `timestep` | number | `1/64` | Seconds per simulated frame, one entity tick. At most 0.125. |

**`get_particles`**

| Argument | Type | Default | Meaning |
| --- | --- | --- | --- |
| `entity` | integer | none | Only systems played by this entity, by id from `find_entities`. |
| `near` | number[3] | none | Only systems within `radius` of this point, nearest first. |
| `radius` | number | `512` | Distance from `near`. |
| `limit` | integer | `20` | Most systems to return. At most 500. |

## Behavior

- Paths inside a package use `vpk:package.vpk:inner/path`. Every `.vpk:` separates a package from the path inside it, so a file in a nested package is `vpk:outer_dir.vpk:maps/inner.vpk:models/file.vmdl_c`. A bare `.vpk` opens the package browser rather than a file.
- The viewer state the tools drive is global, so the server runs one call at a time; a second call waits for the first to finish.
- `open_file` waits for the tab to finish loading before returning, up to `timeout_seconds` (default 180). A crash while loading is reported as the tool's error instead of the call hanging until the timeout.
- `select_tab`, `close_tab`, `open_file`, `clear_selection`, `set_camera`, `set_layer`, `set_physics_group`, `set_render_mode`, `select_entity`, `pick`, `pause` and `resume` draw a fresh frame before answering, so the window shows the result even while it is in the background. This redraw is best effort and never fails the call.
- Pausing freezes entities, particles, animation and shader time, but not the camera. `step` advances a tab's simulation by an exact number of seconds in fixed frames and leaves it paused, so two runs stepped the same way produce identical frames, including the dither pattern. Particles do not emit while paused, so a freshly loaded map needs a `step` before its particle systems have anything in them.
- `screenshot` selects the tab, renders a frame to let the renderer request the textures the view needs, finishes streaming them in, renders `settle_frames` more frames for auto exposure to settle, and only then captures. This works while the window is in the background, but not while it is minimized, which stops rendering entirely. The inline image is a downscaled JPEG (max width 1600px, quality 85); passing `path` additionally writes a full resolution, lossless PNG to disk. A texture or image tab captures the whole image at its selected mip, and a graph tab the whole graph, regardless of pan and zoom.
- Tools that need a 3D scene (camera, render modes, render stats, pick, step, particles, selection) only work on 3D tabs. Texture, image and graph tabs take `screenshot` and `set_viewport`; texture and image tabs also take `reload_shaders`. On any other tab these tools fail with an error that names the tools that do work there, and `get_info` on a tab lists them.
- `set_viewport` renders at an exact size so screenshots from two builds can be compared, capped to the window's own size since the frame is read back out of its own framebuffer.
- `get_log` and `clear_log` return a `cursor`; pass it back as `since` on the next `get_log` call to read only what was logged after it.
- An unhandled exception while an agent drives the viewer is reported through `get_status` (`unhandled_exceptions` count, `last_unhandled_exception` text) instead of opening the error dialog, and the process is kept from terminating so it can still be queried afterward.
- `quit` answers before closing the window, so the caller gets a reply instead of a dropped connection. Call it before rebuilding, since a running instance holds the build output open.
- A tool that acts on a tab that is still loading waits for that load to finish, up to 180 seconds. `list_tabs` and `get_info` mark such tabs with `loading: true`.
- A tool call that needs the UI thread waits up to 20 seconds by default (5 minutes for `reload_shaders`), or up to 180 seconds while a tab is loading. It then fails with an error that names the tab still loading, or, when nothing is loading, says a modal dialog may be open. A call that timed out is dropped rather than run later.

## Example session

Open a map:

```json
{
  "path": "vpk:C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Global Offensive/game/csgo/maps/de_dust2.vpk:maps/de_dust2.vmap_c"
}
```

`open_file` returns the new tab, for example `{"tab": 1, "title": "de_dust2.vmap_c", ...}`.

Move the camera:

```json
{
  "tab": 1,
  "position": [0, 0, 300],
  "angles": [15, 45, 0]
}
```

Capture a screenshot:

```json
{
  "tab": 1,
  "path": "C:/temp/dust2.png"
}
```

Each of these is passed as the `arguments` of a `tools/call` request, as shown in [Connecting](#connecting).
