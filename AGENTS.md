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

## Building

Build all projects: `dotnet build`
When modifying subproject code, only build that subproject `dotnet build Renderer`

## Code Style

Follow standard Microsoft C# conventions. Key rules:

### Formatting
- **Indentation:** 4 spaces (never tabs, no trailing spaces)
- **Line endings:** LF (Unix-style) for C# files
- **Braces:** Opening braces on new lines (Allman style). Always use braces.
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

### AnimGraphEngine context:
- MathF operations instead of (float)Math
- C# implementation of GetValue returns the value directly, instead of updating an instance member.
- Prefer early returns.
- We don't use tasks / tasklist. Pose is computed synchronously on each node.
- C++ has on demand initialize and shutdown of nodes. In C# we initialize all nodes at once when creating the graph.
- Don't bother writing tests.
- We are ignoring SyncTrack, SampledEventRange for now.
- No, we do not initialize and shutdown nodes.
- Maybe we should do Start and Stop that replaces the shutdown/initialize pattern.
- Get***() functions are just properties in C#.
- MathUtils.Saturate() instead of clamp between 0 and 1.


### Expression Bodies
- **Prefer expression bodies** for properties, indexers, accessors
- **Block bodies** for methods, constructors, operators

### Modern C# Features
Use modern patterns:
- Switch expressions over switch statements
- Pattern matching (`is` with type checks)
- Null coalescing (`??`, `??=`)
- Throw expressions
- String interpolation
- Using declarations (not statements when possible)

## Imports and Usings

- **Sort usings:** System namespaces first, then others alphabetically
- **No unnecessary imports:** Remove unused
- **Global usings:** `System`, `System.Numerics`, `System.Collections.Generic` are global (defined in Directory.Build.props)

## Comments and Documentation

- **Avoid obvious/redundant comments** - Code should be self-documenting
- **Only write comments for:** Non-obvious logic, workarounds, TODOs, or explaining "why" not "what"
- **XML docs required** for public APIs in ValveResourceFormat and Renderer library

## Before Committing Checklist

1. Run `dotnet format` to fix formatting
2. If modifying `ValveResourceFormat/` library: Run `dotnet test` to ensure all tests pass
3. Remove any debug code, console logs, commented code
