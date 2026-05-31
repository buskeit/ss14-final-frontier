using Content.Shared.GridControl.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;

namespace Content.Server.GridControl.Systems;

[UsedImplicitly]
public sealed partial class BluespaceParkingSystem : SharedBluespaceParkingSystem
{
    [Dependency] private readonly TransformSystem _transform = default!;


    public override void Initialize()
    {
        base.Initialize();

        InitializeLifecycle();
        InitializeEvents();
        InitializeUI();
    }

}
