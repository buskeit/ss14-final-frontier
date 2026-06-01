using Robust.Shared.GameStates;

namespace Content.Shared.Storage.Components;

/// <summary>
/// Marks a storage entity as a valid target for dumping items from dumpable containers (like trashbags, ore bags).
/// When a dumpable container is used on an entity with this component,
/// compatible items will be transferred into the storage.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DumpTargetComponent : Component
{
}
