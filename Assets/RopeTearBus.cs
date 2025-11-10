using System;
using Obi;

public static class RopeTearBus
{
    /// Broadcast that a rope was torn at a given actor-space particle index.
    /// Args: (rope, tearIndex)
    public static event Action<ObiRopeBase, int> OnAnyRopeTorn;

    public static void Broadcast(ObiRopeBase rope, int tearIndex)
        => OnAnyRopeTorn?.Invoke(rope, tearIndex);
}