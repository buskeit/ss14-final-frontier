using Content.Server.Worldgen.Systems;

namespace Content.Server.Worldgen.Components;

/// <summary>
/// Entities that are should be tracked and despawned whenever the chunk they are located at is unloaded.
/// </summary>
[RegisterComponent]
[Access(typeof(ChunkOwnedEntitySystem))]
public sealed partial class ChunkOwnedEntityComponent : Component
{
    [DataField] public EntityUid OwningChunk;
}

