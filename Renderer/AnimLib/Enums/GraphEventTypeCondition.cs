namespace ValveResourceFormat.Renderer.AnimLib;

enum GraphEventTypeCondition : byte
{
    Entry = 0,
    FullyInState = 1,
    Exit = 2,
    Timed = 3,
    Generic = 4,
    Any = 5,
}
