using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.MCTN.Systems;

public sealed partial class MCTNSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPhysicsSystem _physx = default!;


    public override void Initialize()
    {
        base.Initialize();

        InitializePlugs();
        InitializeConnections();
        InitializeTethers();
        InitializeUI();
    }
}
