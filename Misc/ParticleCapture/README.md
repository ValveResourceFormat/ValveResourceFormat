# ParticleCapture

Renders a particle system at fixed simulation times into PNG files, without the GUI, and prints the
state of every system in its tree along with the diagnostics explaining what keeps it from matching
the game. Runs with a pinned random seed, so the same build gives the same frames.

```
dotnet run --project Misc/ParticleCapture -- <pak01_dir.vpk> particles/x.vpcf [options]
```

| Option | Meaning |
| --- | --- |
| `--times 0.5,1,2` | Simulation times to capture, in seconds |
| `--size 512` | Square image size |
| `--out dir` | Output folder |
| `--fps 60` | Simulation step rate |
| `--seed 1` | Random seed of the system tree |
| `--camera x,y,z --target x,y,z` | Fixed camera instead of framing the effect's bounds |
| `--cp i=x,y,z` | Places a control point |
| `--ground z` | Adds a horizontal collision plane at height z |
| `--map maps/de_dust2.vpk --at x,y` | Loads a map and drops the effect onto its ground at x,y |

```
dotnet run --project Misc/ParticleCapture -- audit <pak01_dir.vpk> out.md
```

Writes an inventory of every particle function class the package's systems use and whether it is
implemented, as in [cs2-compatibility.md](cs2-compatibility.md).
