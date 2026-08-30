# ShaderCompilerBench

Measures every stage of every way a shader could reach OpenGL in this renderer, so the cost of
moving off the driver's own GLSL front end can be argued from numbers rather than folklore.

Each path is timed stage by stage, from the offline compiler parsing its source through to the
driver reporting `GL_LINK_STATUS` and handing back the program binary.

| Path | What it is |
| --- | --- |
| `GLSL -> driver` | What the renderer does today. |
| `GLSL -> glslang -> SPIR-V -> driver` | The same sources, compiled ahead of time. No Slang involved. |
| `Slang -> SPIR-V -> driver` | Slang's own SPIR-V backend. |
| `Slang -> GLSL -> driver` | Slang emitting GLSL for the driver to compile. |
| `Slang -> GLSL -> glslang -> SPIR-V -> driver` | Slang emitting GLSL, turned into SPIR-V offline. |

The two Slang-free paths compile a real renderer shader (`--shader`, `complex` by default). The
Slang paths compile `Shaders/bench.slang`, a forward-lit uber-shader written to be the same class of
workload: layered materials, parallax, a clustered light loop with cascaded shadows, image based
lighting, cloth and skin lobes, fog, and a render mode switch.

## Running

```
dotnet run --project Misc/ShaderCompilerBench -- -n 10 -w 2
```

| Option | Meaning |
| --- | --- |
| `-n`, `--iterations` | Measured iterations per stage (default 10). |
| `-w`, `--warmup` | Unmeasured iterations first (default 2). The driver's compiler backend costs a few hundred milliseconds to spin up once per process, so do not set this to zero. |
| `--shader <name>` | Renderer shader for the Slang-free paths (default `complex`). |
| `--spirv <version>` | SPIR-V version glslang targets (default `1.0`). Shaders using subgroup ops need `1.3`. |
| `--only <substring>` | Run only the paths whose name matches. |
| `-D NAME=VALUE` | Preprocessor macro for `bench.slang`, e.g. `-D MAX_LIGHTS=32`. |
| `--dump` | Write every intermediate (GLSL, SPIR-V) to `bin/<config>/dump`. |
| `--in-process` | Run every path in one process instead of one process each. Crashes, see below. |
| `--probe` | Report what SPIR-V this driver actually accepts, and exit. See below. |

Every compile is stamped with a salt that differs per iteration *and* per run, because the NVIDIA
shader cache lives on disk and would otherwise serve most of the run out of it.

## Requirements

Nothing installed. glslang comes from the `Glslang.NET` package and Slang from `Prowl.Slang`, both
of which carry their own natives. A [Vulkan SDK](https://vulkan.lunarg.com/) install is used instead
when one is present and the package native is not, which is only useful for testing a different
glslang build.

## Shipping the compiler with the client

Compiling GLSL to SPIR-V on the client, rather than letting the driver's GLSL front end do the work,
is a win even before any caching: `csgo_environment` costs 405 ms of driver time as GLSL, against
40 ms of glslang plus 5 ms of driver time as SPIR-V. Cache the SPIR-V and only the 5 ms remains.

What that costs to ship, per runtime identifier, from `Glslang.NET` 1.2.0:

| RID | size | notes |
| --- | --- | --- |
| `win-x64` | 7.5 MB | one DLL, C++ runtime linked statically, so no VC++ redistributable |
| `win-arm64` | 6.7 MB | |
| `linux-x64` | 19.3 MB | ~10 MB of that is unstripped debug info; needs only libc, libm, libpthread, libdl |
| `linux-arm64` | 18.8 MB | |
| `osx-arm64` | 9.7 MB | |

Only the matching identifier ends up in a self-contained publish, so the cost is one number per
download, not the sum. For scale, `ValveResourceFormat` already ships `Vortice.SpirvCross`
(2.1 MB on `win-x64`).

The Vulkan SDK's own glslang build is 7.6 MB plus a second 85 KB library for the default resource
limits, and it links the C++ runtime dynamically, so it is the worse one to redistribute.

### What Slang would cost instead

Sizes on `win-x64`, and what a process actually loads, checked with `Process.Modules`:

| | Prowl.Slang 3.2.1 (used here) | official Slang build |
| --- | --- | --- |
| `slang-compiler` — loaded always | 33.3 MiB | 24.2 MiB |
| `slang-glslang` — loaded for the SPIR-V target | 10.8 MiB | 5.9 MiB |
| `slang-glsl-module` — never loaded, GLSL *input* only | 1.8 MiB | not shipped |
| managed wrapper | 0.13 MiB | |

So Slang is 24-33 MiB to emit GLSL and 30-44 MiB to emit SPIR-V, against 7.1 MiB for glslang alone.
`slang-glslang` is the SPIR-V validator: renaming it away leaves both targets working and takes
about 34 ms off each SPIR-V compile.

The Prowl.Slang natives are a much fatter build of the same version on Windows. Dropping the
official binaries from `Slangc.NET` over them works with the same wrapper, and is faster as well as
smaller — 166 ms against 203 ms for the SPIR-V path. On Linux and macOS the two builds are within a
few percent of each other, both unstripped, around 36-39 MiB.

Slang also writes a `slang-glsl-module.bin` core module cache (1.2 MiB) next to its own library on
first use, which is worth knowing if the install directory is read-only.

## Things worth knowing

- **Which SPIR-V version a driver accepts is not something OpenGL lets you ask.**
  `GL_ARB_gl_spirv` is specified against SPIR-V 1.0, and `GL_SPIR_V_EXTENSIONS` lists extensions,
  not versions. Anything above 1.0 works only because a driver chose to be lenient, so `--probe`
  finds out by compiling and linking rather than by asking. It also reports whether
  `GL_KHR_shader_subgroup` and `GL_ARB_gl_spirv` are both present, which together imply the driver
  has to take 1.3: subgroup arithmetic needs capabilities that were added in SPIR-V 1.3 and never
  had an extension. Run it on every vendor the renderer supports before relying on a version above
  1.0. NVIDIA 595.79 on a GTX 1660 SUPER accepts 1.0 through 1.6, subgroups included.
- **Slang cannot emit SPIR-V 1.0 with its own backend.** It warns *"Slang's SPIR-V backend only
  supports SPIR-V version 1.3 and later"*, then stamps the requested version into the header anyway.
  OpenGL 4.6 asks for SPIR-V 1.0, but NVIDIA accepts the 1.3 modules. Going through glslang is the
  only way to get real 1.0.
- **`Prowl.Slang` 3.2.1 is not memory safe.** Calling `GetBuildTagString` before a session, or
  creating sessions for different targets in one process, faults inside the native library. This is
  why each path runs in its own process and why the build tag is printed last.
- **The renderer's GLSL is not portable to glslang.** `complex.frag.slang` uses `defined()` inside
  object-like macros, which is undefined behaviour that NVIDIA accepts and glslang rejects. Other
  shaders trip over `gl_DepthRange` (`grid`) or subgroup ops needing SPIR-V 1.3
  (`csgo_environment`). `--shader default` and `--shader sky` go all the way through.
- **Handing the driver SPIR-V instead of GLSL is worth around two orders of magnitude.** Measured
  driver time for the same shader, producing a program binary of the same size either way:

  | shader | preprocessed | GLSL -> driver | glslang -> SPIR-V -> driver | glslang itself | program binary |
  | --- | --- | --- | --- | --- | --- |
  | `default` | 4.1 KiB | 5.8 ms | 0.20 ms | 3.6 ms | 15.9 / 16.4 KB |
  | `sky` | 14.0 KiB | 12.0 ms | 0.26 ms | 7.5 ms | 17.8 / 18.9 KB |
  | `csgo_environment` | 171.8 KiB | 405 ms | 4.9 ms | 40 ms | 464 / 486 KB |

  The glslang half is cacheable on disk, and even paid every time it still beats the driver's own
  front end. `complex` cannot be measured this way until its macros are fixed, but it is the same
  size and shape as `csgo_environment`.

  A successful link is not proof the driver finished, so every path also draws with the program it
  just built. NVIDIA does specialize again at first draw, but the amount is small and does not
  change the picture — for `csgo_environment`, first draw is 0.4 ms after the GLSL path and 1.4 ms
  after the SPIR-V path, against 401 ms and 3.6 ms of link. The second draw is 0.04 ms either way.
  Only one render state is exercised, so a shader drawn with several states pays that 1 ms more than
  once; that is true of both paths.
- **`bench.slang` is much cheaper for the driver to link than `complex`**, even though the two are
  a similar size in source. `-D UNROLL_LIGHTS=1 -D MAX_LIGHTS=64` turns the light loop into 64
  copies of its body and brings driver link time into the same order of magnitude; past that the
  shader stops fitting in the register file. The report splits offline compiler time from driver
  time for exactly this reason: only the compiler half is comparable across differently sized
  shaders.
