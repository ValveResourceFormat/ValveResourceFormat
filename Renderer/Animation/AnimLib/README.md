# AnimLib

Runs Source 2's Animgraph 2 animation graphs (`.vnmgraph`): state machines, blends, layers, sync
tracks, events, root motion, warping and IK.

Animgraph 2 is taken from the [Esoterica](https://github.com/BobbyAnguelov/Esoterica) animation
system, and this is a C# port of it. A node behaves as the Esoterica node of the same name does.

| Folder         | What is in it                                                                                                          |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `Definitions/` | The data each node, event and resource is compiled to. Generated from Valve's schema by `Misc/SchemaConverter`.        |
| `Runtime/`     | What the nodes and the data they read do. One file per node family, per large node, or per data type.                 |
| here           | What the nodes share: poses, blending, easing, 2D blend spaces, sampled events, two bone IK and transform helpers.     |

A graph is played through `AnimationGraph` in the folder above, which builds the nodes, feeds them
their control parameters and hands the resulting pose to the model.

Source 2 adds a few nodes Esoterica does not have, in `Runtime/ValveNodes.cs`, with the opportunity of
client nodes too.
