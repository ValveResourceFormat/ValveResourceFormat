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

The two Slang-free paths compile a real renderer shader (`--shader`, `csgo_environment` by
default, being the largest one that survives every path). The
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
| `--shader <name>` | Renderer shader for the Slang-free paths (default `csgo_environment`). `--list` prints them all. |
| `--spirv <version>` | SPIR-V version glslang targets. Defaults to `auto`, which is the newest the driver accepts; pass `1.0` to measure the portable floor. |
| `--bindings <mode>` | `separate` (default) gives each resource class its own binding range; `overlapping` leaves glslang's default numbering. See below. |
| `--only <index or substring>` | Run one path. The names overlap, so an index from `--list` is the unambiguous form. |
| `-D NAME=VALUE` | Preprocessor macro for `bench.slang`, e.g. `-D MAX_LIGHTS=32`. |
| `--dump` | Write every intermediate (GLSL, SPIR-V) to `bin/<config>/dump`. |
| `--in-process` | Run every path in one process instead of one process each. Faster, but the second Slang path then starts with a warm core module and looks cheaper than it is. |
| `--probe` | Report what SPIR-V this driver actually accepts, and exit. See below. |
| `--list` | Print the path names `--only` matches and the shader names `--shader` takes, and exit. Needs no GPU. |

Every compile is stamped with a salt that differs per iteration *and* per run, because the NVIDIA
shader cache lives on disk and would otherwise serve most of the run out of it. The salt has to
reach whatever the driver is handed: `bench.slang` reads `BENCH_SALT` in real arithmetic, and
GLSL bound for the driver gets a `#define`, which is enough because the driver hashes the source
text. GLSL bound for glslang gets a specialization constant as well, because a define nobody reads
does not survive into the SPIR-V and the driver would then be handed the same module every
iteration. That is an easy mistake to make and a quiet one: the driver reports a few milliseconds
for a shader that really costs it several hundred, and the program binary it hands back is the
same size either way. Checking that `--dump` output differs between two salts is the test.

To find out whether one shader survives one path on an unfamiliar driver, name both and turn the
repetition off. Warmup iterations count, so a shader that fails fails during them:

```
ShaderCompilerBench --probe
ShaderCompilerBench --list
ShaderCompilerBench --only 0 --shader csgo_environment -n 1 -w 0 --dump
```

Each path runs in its own process, so one failing path does not stop the others; it is reported as
`failed` in the summary with the compiler or driver's own message above it. `--dump` writes the
exact GLSL and SPIR-V that were handed over, which is what to attach to a bug report.

A run needs an OpenGL 4.6 context, which is what the renderer itself requires.

### Binding ranges

glslang numbers every resource class from zero, because OpenGL gives each its own namespace. For
`csgo_environment` that produces, in the fragment stage:

| class | bindings |
| --- | --- |
| `UniformConstant` (samplers) | 0-9 |
| `Uniform` (uniform blocks) | 0, 1, 2, 6, 7 |
| `StorageBuffer` (storage blocks) | 0, 1, 10, 11, 12 |

Three things on binding 0. Correct for OpenGL, and NVIDIA accepts it, but a driver validating the
module the way Vulkan would sees one descriptor set with a collision in it and can reject it with
nothing to say. So `--bindings separate`, the default, moves the buffer classes clear:

    samplers from 0, uniform blocks from 32, storage blocks from 48

The bases come from `GL_MAX_TEXTURE_IMAGE_UNITS`, `GL_MAX_UNIFORM_BUFFER_BINDINGS` and
`GL_MAX_SHADER_STORAGE_BUFFER_BINDINGS` rather than from constants, because OpenGL only promises
eight storage block bindings and a fixed base would be off the end of a conforming driver. A class
with nowhere to go stays at zero and the report says so. It costs nothing measurable: the driver's
link of `csgo_environment` lands in the same 370-420 ms band either way on NVIDIA, which is inside
its run-to-run spread.

Anything reading these bindings back has to agree with them, so a renderer adopting this would take
its binding points from the reflection rather than from a constant.

### When the driver rejects SPIR-V

Drivers are allowed to fail with an empty info log, and several do. The message says so rather than
printing a bare colon, and adds the GL error code and the module sizes. From there:

- `--spirv 1.0` compiles the portable floor instead of whatever `auto` detected. If that works, the
  driver's problem is with a newer construct rather than with the shader.
- `--dump` writes the rejected module to `bin/<config>/dump`, ready for `spirv-val` and `spirv-dis`
  from the Vulkan SDK.
- `--bindings overlapping` restores glslang's own numbering, which is the layout described below.
  If a driver takes one mode and not the other, that is the answer.

## Requirements

Nothing installed. glslang comes from the `Glslang.NET` package and Slang from `SlangShaderSharp`,
both of which carry their own natives. A [Vulkan SDK](https://vulkan.lunarg.com/) install is used instead
when one is present and the package native is not, which is only useful for testing a different
glslang build.

## Shipping the compiler with the client

Compiling GLSL to SPIR-V on the client, rather than letting the driver's GLSL front end do the work,
does not save compile time on NVIDIA. `csgo_environment` costs about 260 ms of driver time as GLSL,
against 22 ms of glslang plus 370-430 ms of driver time as SPIR-V (RTX 4070 SUPER, driver 616.56).
The driver's back end is where the time goes, and it runs either way; handing it SPIR-V only
replaces its front end, which is the cheap part. Caching the SPIR-V removes the 22 ms and nothing
else. A compiled program cache has to hold what `glGetProgramBinary` returns, and that works the
same from GLSL as from SPIR-V. The case for shipping a compiler has to rest on something other
than compile time: a front end that behaves the same on every driver, or a shading language the
driver cannot read.

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

| | size | loaded |
| --- | --- | --- |
| `slang-compiler` | 24.2 MiB | always |
| `slang-glslang` | 5.9 MiB | for the SPIR-V target only |
| managed wrapper | 0.31 MiB | always |

So Slang is 24 MiB to emit GLSL and 30 MiB to emit SPIR-V, against 7.1 MiB for glslang alone.
`slang-glslang` is the SPIR-V validator: renaming it away leaves both targets working and takes
about 34 ms off each SPIR-V compile.

This is the official Slang build, which `SlangShaderSharp` ships. `Prowl.Slang`, the other .NET
binding, builds the same version much fatter on Windows — 33.3 MiB and 10.8 MiB, plus a
`slang-glsl-module` that never loads because it only serves GLSL *input* — and is measurably slower
with it, 203 ms against 167 ms for the SPIR-V path. On Linux and macOS the two builds are within a
few percent of each other, both unstripped, around 36-39 MiB.

Slang writes a core module cache next to its own library on first use, which is worth knowing if the
install directory is read-only.

## Things worth knowing

- **Which SPIR-V version a driver accepts is not something OpenGL lets you ask.**
  `GL_ARB_gl_spirv` is specified against SPIR-V 1.0, and `GL_SPIR_V_EXTENSIONS` lists extensions,
  not versions. Anything above 1.0 works only because a driver chose to be lenient, so `--probe`
  finds out by compiling and linking rather than by asking. It also reports whether
  `GL_KHR_shader_subgroup` and `GL_ARB_gl_spirv` are both present, which together imply the driver
  has to take 1.3: subgroup arithmetic needs capabilities that were added in SPIR-V 1.3 and never
  had an extension. Run it on every vendor the renderer supports before relying on a version above
  1.0.

  A driver taking a version is not the same as it taking a shader written in it. NVIDIA 595.79 on a
  GTX 1660 SUPER accepts an empty 1.6 module, but rejects one that discards: SPIR-V 1.6 deprecated
  `OpKill`, glslang emits `OpTerminateInvocation` instead once the target reaches 1.6, and the GL
  driver answers `SPIR-V: Invalid opcode`. So the probe compiles something a real shader would
  contain rather than an empty `main`, and `auto` lands on 1.5 there rather than 1.6. Between 1.0
  and 1.5 the version makes no measurable difference to compile time: `sky` is 3.2 ms of glslang
  and 6.3 ms of driver at 1.0, against 2.9 ms and 5.5 ms at 1.5, which is inside the noise.
- **Slang cannot emit SPIR-V 1.0 with its own backend.** It warns *"Slang's SPIR-V backend only
  supports SPIR-V version 1.3 and later"*, then stamps the requested version into the header anyway.
  OpenGL 4.6 asks for SPIR-V 1.0, but NVIDIA accepts the 1.3 modules. Going through glslang is the
  only way to get real 1.0.
- **The renderer's GLSL is not portable to glslang.** `complex.frag.slang` uses `defined()` inside
  object-like macros, which is undefined behaviour that NVIDIA accepts and glslang rejects. Other
  shaders trip over `gl_DepthRange` (`grid`) or subgroup ops needing SPIR-V 1.3
  (`csgo_environment`). `--shader default` and `--shader sky` go all the way through.
- **Handing the driver SPIR-V instead of GLSL does not make the driver faster.** Measured driver
  time for the same shader on an RTX 4070 SUPER with driver 616.56, producing a program binary of
  about the same size either way. The driver column is everything from upload to the second draw;
  the link is where nearly all of it goes:

  | shader | preprocessed | GLSL -> driver | of which link | glslang -> SPIR-V -> driver | of which link | glslang itself | program binary |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | `default` | 4.1 KiB | 4.2 ms | 2.4 ms | 4.3 ms | 2.6 ms | 1.7 ms | 15.9 / 16.4 KB |
  | `sky` | 14.0 KiB | 7.9 ms | 4.9 ms | 5.5 ms | 3.7 ms | 2.9 ms | 17.8 / 18.9 KB |
  | `csgo_environment` | 171.8 KiB | 267 ms | 254 ms | 432 ms | 418 ms | 22 ms | 464 / 486 KB |

  The small shaders are a wash. The big one is slower through SPIR-V, and not by a little: the
  link of glslang's module took 367-445 ms across ten iterations against 218-300 ms for the GLSL.
  glslang here runs no optimizer, so the driver gets a literal translation of the source and has
  to do everything the GLSL front end would have folded on the way in; whether `spirv-opt` closes
  that gap is untested. `complex` cannot be measured this way until its macros are fixed, but it
  is the same size and shape as `csgo_environment`.

  A successful link is not proof the driver finished, so every path also draws with the program it
  just built. For `csgo_environment`, first draw is 0.5 ms after the GLSL path and 0.6 ms after the
  SPIR-V path, and the second draw is 0.6 ms either way, so nothing is being deferred to the first
  frame. Only one render state is exercised.
- **`bench.slang` costs the driver about two thirds of what `csgo_environment` does**, and it costs
  the same however it arrives. On the same machine as the table above, the driver spent 184 ms on
  Slang's SPIR-V, 177 ms on Slang's GLSL and 196 ms on that GLSL put through glslang, of which
  163-181 ms was the link and 7-9 ms a first draw that really does specialize, against 267 ms for
  `csgo_environment` as GLSL. Slang's own work is 76-85 ms on top, dominated by parsing and
  checking the module (30 ms) and emitting the fragment stage (34 ms). `-D UNROLL_LIGHTS=1 -D
  MAX_LIGHTS=64` turns the light loop into 64 copies of its body for a heavier driver workload;
  past that the shader stops fitting in the register file. The report splits offline compiler time
  from driver time because only the compiler half is comparable across differently sized shaders.
