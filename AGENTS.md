## Project Overview

ValveResourceFormat (VRF) is a C# library and toolset for parsing Valve's Source 2 resource formats. The project folders are:
- **ValveResourceFormat/** - Core parsing library published to NuGet
- **GUI/** - WinForms-based viewer application
- **CLI/** - Command-line decompiler and file viewer
- **Renderer/** - OpenGL rendering engine for Source 2 assets.
  - Shaders use `.slang` extension (`.frag.slang`, `.vert.slang`) with GLSL syntax.
  - Shader files must only contain ASCII characters.
- **Tests/** - NUnit test suite for the ValveResourceFormat library.
  - Only run tests when changing code in `ValveResourceFormat/`. Other projects are not covered.
  - Run with `dotnet test` - tests are fast, no need to filter.
  - Test files use NUnit with `[TestFixture]` and `[Test]` attributes.

**Target:** Latest released .NET. Use modern C# features. Nullable reference types enabled.

### Shader Pipeline
- Each Source 2 `.vfx` shader name is mapped via `GetShaderFileByName()` to one of our shader files (e.g. `vr_complex.vfx` → `complex`, `csgo_environment_blend.vfx` → `csgo_environment`). Unmapped shaders fall back to `complex`.
- During compilation, a `GameVfx_{vfxName}` define is set to 1 (e.g. `GameVfx_vr_complex`), activating shader-specific code paths via `#if` blocks. All other `GameVfx_` defines remain 0.
- Texture names from materials are matched to shader uniforms. An alias system maps Source 2 texture names to our uniform names when they differ.
- Material float/int/vector params are set as uniforms by iterating the shader's default values and overriding with material values.
- Render mode defines (e.g. `renderMode_Illumination`) default to 0 and are overridden via static combos at compile time.

## Code Style
Follow standard Microsoft C# conventions. Key rules:

### Formatting
- **Indentation:** 4 spaces (never tabs, no trailing spaces)
- **Line endings:** LF (Unix-style) for C# files
- **Braces:** Opening braces on new lines (Allman style)
- **Final newline:** Required in all files

### Naming Conventions
- Types: PascalCase (`ResourceData`, `ClosedCaption`)
- Methods/Properties: PascalCase (`GetResourceType()`, `FileName`)
- Private fields: PascalCase
- Parameters/Variables: camelCase (`resourceType`, `blockIndex`)
- Interfaces: IPascalCase (`IDisposable`)
- Namespaces: Match folder structure loosely (not strictly enforced)

### Types and Variables
- **Always use `var`** for local variables (built-in types, apparent types, everywhere)
- **Use collection expressions:** `[]` instead of `new List<>()`
- **Nullable types:** Use `?` appropriately (`string?`, `Resource?`)
- **No `this.` qualification** unless disambiguating

### Expression Bodies
- **Prefer expression bodies** for properties, indexers, accessors
- **Block bodies** for methods, constructors, operators

### Modern C# Features
- Use modern patterns
- Switch expressions over switch statements
- Pattern matching (`is` with type checks)
- Null coalescing (`??`, `??=`)
- Throw expressions
- String interpolation
- Using declarations (not statements when possible)
- `MathF` operations over `(float)Math` casts
- Prefer early returns in methods

### Imports and Usings
- **Sort usings:** System namespaces first, then others alphabetically
- **No unnecessary imports:** Remove unused
- **Global usings:** `System`, `System.Numerics`, `System.Collections.Generic` are global (defined in Directory.Build.props)

### Comments and Documentation
- **Use `//` comments** instead of `/* */` block comments
- **Avoid obvious/redundant comments** - Code should be self-documenting
- **Only write comments for:** Non-obvious logic, workarounds, TODOs, or explaining "why" not "what"
- **XML docs required** for public APIs in ValveResourceFormat and Renderer library
  - Keep XML docs concise and to the point
  - For overridden methods, use `<inheritdoc/>` if no new information is needed

## Before Committing Checklist

1. Run `dotnet build` and fix warnings and notices
2. Run `dotnet format` to fix formatting
3. If modifying `ValveResourceFormat/` library: Run `dotnet test` to ensure all tests pass
4. Remove any debug code, console logs, commented code
