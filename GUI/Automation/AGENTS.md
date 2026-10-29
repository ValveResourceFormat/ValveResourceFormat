## MCP automation server

Lets an agent drive the viewer over MCP: `Source2Viewer.exe --mcp[=port]`, debug builds only. User docs are in `docs/guides/mcp-server.md`.

### Footprint

The goal is a powerful server that barely touches the rest of the viewer.

- Everything here is `#if DEBUG`. Code outside this folder only calls `Automation.cs`. Its `IsEnabled` is a constant `false` in Release, so the checks compile away.
- Prefer to do a job in the tool, from what the viewer already exposes, instead of adding a hook to viewer or renderer code. A hook needs a reason that only automation has, such as determinism or not blocking on a dialog. A bug fix that is not only for automation belongs in its own change.
- The hooks outside this folder:
  - startup and window: `Program`/`MainForm`, no focus stealing, no IPC forwarding.
  - opening tabs: tab-returning open methods, `ExportData.Loaded`.
  - log and errors: `Log` feeds `AutomationLog`, and `ErrorReport` sends dialogs to `ReportedErrors`.
  - `Settings.Save` is skipped.
  - frame requests: the `RenderLoopThread` partial methods, implemented in `RenderLoopThread.Frames.cs`.
  - the simulation clock in `GLSceneViewer.OnPaint`.
  - the world simulation state, `GLWorldViewer.QueueSimulation`, which queues Play, Pause or Stop without the button's focus change.
  - sidebar names: `AccessibleName` set where controls are created.
- Get information from what the viewer already does before you add state to it. For example, the tools read viewer load failures from the `CodeTextBox` "Failed to open" log line.

### Protocol

- `McpServer` speaks only the MCP revision in `ProtocolVersion`. That revision is stateless: there is no handshake, and every request has the version in its `_meta` and in headers. A request with a different version gets an error that names the supported one. A request without the version, the client capabilities or the headers is taken to speak `ProtocolVersion`, so a plain HTTP script can call a second viewer, but a header that is sent must match the body.
- It listens only on loopback, and rejects non-loopback hosts and origins to stop DNS rebinding.
- `notifications/cancelled` cancels a running call through its token.

### Tools

- A tool is a `[Tool]` method of `McpTools`, with its name in snake_case. Its parameters make the input schema. Every parameter needs a `[Description]`, `[Range]` sets bounds, and a default makes a parameter optional. The server also accepts camelCase argument names.
- A `TabPage` or viewer parameter makes the tool act on a tab: the active one, unless `tab` is given. A viewer parameter waits for the load and checks the viewer type.
- Threading: a method that returns `Task` runs on the thread pool. A sync method on a 3D viewer runs between frames with the GL context (`WithGl`). All other methods run on the UI thread (`OnUi`). Calls run one at a time, unless the tool is `Concurrent`.
- `Redraws` draws a frame after the call, so the window shows the change even in the background. Return `UnchangedReply` when nothing changed.
- For failures that the caller can fix, throw `ToolException` or `ArgumentException`. They return to the caller as `isError` text. The server logs all other exceptions as bugs.
- Replies are compact JSON: snake_case, no nulls or empty collections, and rounded floats. Use `Flag` and `NonZero` for optional values.
- Ids for tabs, nodes and spawned systems are in weak tables. An id lasts as long as its object, and never keeps the object alive.

### Determinism

Screenshots must be repeatable, so nothing that a tool can capture can depend on wall time or `Random`:

- `pause` and `step` replace the frame time through `AutomationClock`, which also holds the dither still.
- Auto exposure holds on frames that advance no time.
- Material sort ids come from the material name.
- The particle seed comes from the effect name and placement. `set_particles` pins a seed for every replay.
- Camera moves are instant (`SaveCameraForTransition(0)`), because transitions run on scene time.

### Checking changes

Run the Debug build with `--mcp=<port>` and drive it with a small script. Use a free port, so several viewers can run at the same time. Call `quit` before you rebuild, because a running viewer locks the build output.
