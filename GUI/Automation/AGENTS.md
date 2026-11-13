## MCP automation server

Lets an agent drive the viewer over MCP: `Source2Viewer.exe --mcp[=port]`, debug builds only. User docs are in `docs/guides/mcp-server.md`.

### Footprint

- Everything here is `#if DEBUG`. Code outside this folder only calls `Automation.cs`, whose `IsEnabled` is a constant `false` in Release.
- Do a job in the tool, from what the viewer already exposes, before you add a hook or state to viewer or renderer code. A hook needs a reason that only automation has, such as determinism or not blocking on a dialog. A bug fix that is not only for automation belongs in its own change.
- The hooks outside this folder:
  - startup and window: `Program`/`MainForm`, no focus stealing, no IPC forwarding.
  - opening tabs: tab-returning open methods, `ExportData.Loaded`.
  - log and errors: `Log` feeds `AutomationLog`, and `ErrorReport` sends dialogs to `ReportedErrors`. Viewer load failures come from the `CodeTextBox` "Failed to open" log line.
  - `Settings.Save` is skipped.
  - frame requests: the `RenderLoopThread` partial methods in `RenderLoopThread.Frames.cs`.
  - the simulation clock in `GLSceneViewer.OnPaint`.
  - `GLWorldViewer.QueueSimulation`, which queues Play, Pause or Stop without the button's focus change.
  - sidebar names: `AccessibleName` set where controls are created.

### Protocol

- `McpServer` speaks only the MCP revision in `ProtocolVersion`, which is stateless. A request with a different version gets an error that names the supported one. A request without the version or the headers is taken to speak `ProtocolVersion`, so a plain HTTP script works, but a header that is sent must match the body.
- It listens only on loopback, and rejects non-loopback hosts and origins.
- `notifications/cancelled` cancels a running call through its token.

### Tools

- A tool is a `[Tool]` method of `McpTools`, named in snake_case. Its parameters make the input schema: each needs a `[Description]`, `[Range]` sets bounds, and a default makes it optional and goes into the schema. The server also accepts camelCase argument names.
- A `TabPage` or viewer parameter makes the tool act on a tab: the active one, unless `tab` is given. A viewer parameter waits for the load and checks the viewer type.
- Threading: a method that returns `Task` runs on the thread pool. A sync method on a 3D viewer runs between frames with the GL context. All other methods run on the UI thread. Calls run one at a time, unless the tool is `Concurrent`.
- `Redraws` draws a frame after the call. Return `UnchangedReply` when nothing changed.
- For failures that the caller can fix, throw `ToolException` or `ArgumentException`. The server logs all other exceptions as bugs.
- Ids for tabs, nodes and spawned systems are in weak tables, so an id lasts as long as its object.

### Keep it concise

An agent pays for every character of the tool list and of each reply.

- Descriptions: say what the tool does and what prevents a wrong call (units, ranges, what must be true first, pitfalls). Do not list reply keys that explain themselves, repeat a fact that another tool or the schema states, or explain how the viewer works.
- Replies: compact snake_case JSON with rounded floats. Nulls and empty collections drop out by themselves. Leave out defaults with `Flag` and `NonZero`, but keep a false or a zero that is the answer.
- Do not echo arguments back. Keep objects flat (`Merge` joins two), keys short, and data structured instead of joined into text.
- Bulk is opt-in: return ids or counts, and put the detail behind a parameter.
- Errors: one or two short sentences that end with the next step.

### Determinism

Nothing that a tool can capture can depend on wall time or `Random`:

- `pause` and `step` replace the frame time through `AutomationClock`, which also holds the dither still.
- Auto exposure holds on frames that advance no time.
- Material sort ids come from the material name.
- The particle seed comes from the effect name and placement. `set_particles` pins a seed for every replay.
- Camera moves are instant (`SaveCameraForTransition(0)`), because transitions run on scene time.

### Checking changes

Run the Debug build with `--mcp=<port>` on a free port and drive it with a small script. Call `quit` before you rebuild, because a running viewer locks the build output.
