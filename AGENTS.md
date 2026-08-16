## Project Overview

ValveResourceFormat (VRF) is a C# library and toolset for parsing Valve's Source 2 resource formats. The solution file is `ValveResourceFormat.slnx`.

The project folders are:
- **ValveResourceFormat/**: Core parsing library published to NuGet
- **GUI/**: WinForms viewer application
- **CLI/**: Command-line decompiler and file viewer
- **Renderer/**: OpenGL rendering engine for Source 2 assets.
  - Shaders use the `.slang` extension (`.frag.slang`, `.vert.slang`) with GLSL syntax, and must only contain ASCII characters.
  - After changing shaders, run `dotnet run --project Misc/ShaderValidator -- <name filter>` to compile them and their combos on a real GL context. `complex` has combinatorially many combos and is far too slow to validate interactively, so iterate against a smaller shader.
- **Tests/**: TUnit test suite for the ValveResourceFormat library, plus some headless Renderer logic tests in `Tests/Renderer/`.
  - Run tests when changing code in `ValveResourceFormat/` or `Renderer/`. GUI and CLI are not covered.
  - Tests are fast, run the whole suite with `dotnet test`. If it reports `Zero tests ran` (exit code 5), do a full `dotnet build` and retry.
  - When a parsing change legitimately alters text output, run tests with `VRF_REGEN_FIXTURES=1` to rewrite the mismatching `Tests/Files/ValidOutput` dumps in the source tree.
- **Misc/**: Auxiliary tools (ShaderValidator, RenderTest, etc.) in their own solution `Misc/MiscVrfProjects.slnx`.

**Target:** Latest released .NET. Use modern C# features. Nullable reference types enabled.

### Shader Pipeline
- Each Source 2 `.vfx` shader name is mapped via `GetShaderFileByName()` to one of our shader files (e.g. `vr_complex.vfx` → `complex`, `csgo_environment_blend.vfx` → `csgo_environment`). Unmapped shaders fall back to `complex`.
- During compilation, a `GameVfx_{vfxName}` define is set to 1 (e.g. `GameVfx_vr_complex`), activating shader-specific code paths via `#if` blocks. All other `GameVfx_` defines remain 0.
- Texture names from materials are matched to shader uniforms. An alias system maps Source 2 texture names to our uniform names when they differ.
- Material float/int/vector params are set as uniforms by iterating the shader's default values and overriding with material values.
- Render mode defines (e.g. `renderMode_Illumination`) default to 0 and are overridden via static combos at compile time.

### Transforms and Angles
All angle, quaternion and direction conversions live in `EntityTransformHelper`. Use it instead of hand-rolling trig, and read its class remarks before touching this area.
- Source 2 is Z-up and right-handed: +X forward, +Y left, +Z up.
- An entity's `angles` is a QAngle: (pitch, yaw, roll) in degrees, pitch positive **downwards**.
- Matrices are row-vector, so a rotation's first row is forward. Frames built from a direction must put it on +X.
- `Camera` holds the same angles in radians. Convert at that boundary via `Camera.SetFromQAngle`/`GetQAngle`.

## Code Style
Follow standard Microsoft C# conventions. Key rules:

### Formatting
- 4 space indentation, no tabs, no trailing spaces
- LF line endings for C# files, final newline required
- Allman braces (opening brace on a new line)

### Naming
- PascalCase for types, methods, properties, and private fields
- camelCase for parameters and locals, IPascalCase for interfaces
- Namespaces loosely match folder structure

### Language Use
- Always use `var` for locals
- Collection expressions: `[]` instead of `new List<>()`
- Nullable annotations where appropriate (`string?`, `Resource?`)
- No `this.` qualification unless disambiguating
- Expression bodies for properties, indexers, and accessors; block bodies for methods and constructors
- Switch expressions, pattern matching, null coalescing, throw expressions, string interpolation
- Using declarations rather than using statements when possible
- `MathF` operations over `(float)Math` casts
- Prefer early returns
- Sort usings with System namespaces first, then others alphabetically, and remove unused ones
- `System`, `System.Numerics`, `System.Collections.Generic` are global usings (defined in Directory.Build.props)

### Comments and Documentation
- Use `//` comments, and only for non-obvious logic, workarounds, and TODOs; explain "why", not "what"
- Plain ASCII only: no em-dashes, curly quotes, ellipsis, or Unicode math symbols
- Never mention where format knowledge came from (other codebases, tools, games' internals) in comments or commit messages
- Comments must not narrate the change, this conversation, or session codenames; no decorative dividers
- Leave existing comments alone if they are clear and correct
- XML docs are required for public APIs in ValveResourceFormat and Renderer; keep them concise and use `<inheritdoc/>` on overrides that add nothing new

## Before Committing Checklist

Run these once when the work is done, not after every edit. While iterating, build only the project you changed.

1. Run `dotnet build` and fix warnings and notices. CI builds Release, which enables `TreatWarningsAsErrors` and `AnalysisMode=All`, so build with `-c Release` to catch what Debug misses.
2. Run `dotnet format` to fix formatting
3. Run `dotnet test` to ensure all tests pass
4. Remove any debug code, console logs, and commented code you added
