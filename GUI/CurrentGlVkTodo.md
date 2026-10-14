# Current GL to Vulkan Migration Tasks

This file tracks the current state of work on migrating the GUI from OpenGL to Vulkan. It is intended for later agents who need context on what has been discussed and what remains.

---

## Completed

### Conceptual documentation (`GlToVulkan.md`)
All 10 major sections have been written with detailed GL vs Vulkan comparisons:

1. **Windowing / Embedding Model** — Reparented GLFW window → direct swapchain against WinForms control's HWND. Detailed the full reparenting sequence (create hidden window, strip styles, `SetParent`, resize) and how Vulkan eliminates it entirely.
2. **Context / Thread Model** — Per-thread "current context" dance (`MakeCurrent`/`MakeNoneCurrent`) → queue-based submission with no per-thread binding. Documented the multi-threaded deadlock avoidance in `InitializeLoadCore()` and why it becomes irrelevant.
3. **Framebuffer / Render Target Model** — Stateful FBO bindings (MainFramebuffer + GLDefaultFramebuffer) → dynamic rendering (`vkCmdBeginRendering`) with separate color/depth images and explicit blit commands.
4. **Pixel Readback** — `glReadPixels` synchronous readback → staging buffers + fence cycles. Noted as the largest category of code changes (6+ files). Left as-is for now, will address later.
5. **SkiaSharp GL Backend** (`GLGraphViewer`) — `GRGlInterface.Create()` → `GRContext.CreateGl()` → `GrBackendRenderTarget` → `SkiaSharp.Vulkan.SharpVk` with `GrVkBackendContext`.
6. **Input Handling** — Raw mouse capture via GLFW's per-window `CursorState.Grabbed` + `RawMouseInput` → WinForms message filtering + Windows raw input API (`RegisterRawInputDevices`, `WM_INPUT`). Not a blocker; SDL + different UI system planned long-term.
7. **Fullscreen Mode (F11)** — Reparenting GLControl into fullscreen Form → swapchain recreation against borderless HWND or dedicated fullscreen mode.
8. **Debug / Validation** — `glDebugMessageCallback` → Vulkan validation layers with `VK_DEBUG_REPORT_MESSAGE_TYPE_*`.
9. **Query Objects (GPU Timing)** — `glBeginQuery/EndQuery` → timeline semaphores or query pools (`VkQueryPool`).
10. **Swapchain / Buffer Swap** — `glfwSwapBuffers()` → `vkQueuePresentKHR` with semaphore synchronization.

---

## Open Questions (from GlToVulkan.md)

| Question | Status |
|----------|--------|
| Will we still use GLFW at all? | Unresolved. If only for input, many blockers disappear. If eliminated entirely, Win32 native windowing takes over. |
| Does the Renderer library already have a Vulkan backend? | Unresolved. If yes, GUI changes are mostly surface creation and pixel readback abstraction. If not, this is a much larger project. |
| How do we handle multiple viewers simultaneously? | Unresolved. Each viewer currently has its own GLFW window + context. With Vulkan, each would need its own swapchain or shared swapchain with synchronization. |
| What about the thumbnail renderer's offscreen window? | Unresolved. Currently creates a hidden GLFW window for isolated rendering. Would need an offscreen VkSurfaceKHR equivalent — possibly just render to images without any surface at all. |

---

## Next Steps (TBD)

### Phase 1: Remove GL from GUI, decouple from Renderer
This is the primary and first task. The GUI currently has deep coupling to both OpenGL APIs and the Renderer library's GL-specific types. Before any Vulkan work can begin, this coupling must be removed.

- [ ] Identify all direct `GL.*` calls in the GUI (scattered across 6+ files: screenshots, pixel export, thumbnail rendering, texture decoder)
- [ ] Abstract or remove the reparenting mechanism (`GLControl.cs`, `NonportableReparent`) — no longer needed without GLFW windows
- [ ] Replace Skia GL backend with abstracted surface path (or remove entirely)
- [ ] Remove `GLLockScope`, `RenderLoopThread`, and the per-thread context migration dance — all GL-specific abstractions
- [x] Exclude GL-only files via `<Compile Remove>` (marked `VKTODO`), keeping the files identical to master so rebases apply cleanly. Re-enable by deleting the line once a Vulkan equivalent exists.
  - `GUI.csproj`: `GLLockScope`, `GLTextureDecoder` (GUI texture decoding falls back to the CPU path, like the CLI), `GLFWSurface`, and the GL thumbnail renderers `ThumbnailMaterialRenderer`, `ThumbnailModelRenderer`, `ThumbnailParticleRenderer`, `ThumbnailSnapshotRenderer` (texture and SVG thumbnails still work)
  - `Tests.csproj`: `AnimationControllerTest`, `SampledAnimationEventsTest` (animation not ported yet), `ShaderRegistryTest` (tests the GL shader parser; registry and mapping parts could be ported to Slang)
- [ ] Decouple GUI from Renderer's GL types (`Framebuffer`, `RendererContext`, `GLEnvironment`) — either by removing those dependencies or abstracting them behind a surface interface
- [ ] Remove GLFW window creation/management from `GLBaseControl.InitializeLoadCore()` and the thread affinity dance
- [ ] Replace raw mouse capture (`CursorState.Grabbed`) with WinForms + Windows raw input API

### Phase 2: Vulkan migration (after GL is removed)
Only after Phase 1 is complete.

- [ ] Prototype new embedding model: create a Vulkan swapchain against a WinForms control's HWND
- [ ] Design pixel readback abstraction layer (staging buffers + fence cycles) — affects 6+ files
- [ ] Migrate `GLGraphViewer` from GL backend to `SkiaSharp.Vulkan.SharpVk`
- [ ] Replace raw mouse capture with WinForms + Windows raw input API
