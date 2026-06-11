using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Numerics;

namespace Content.Server.GameTicking;

[RegisterComponent]
public sealed partial class PersistentLocationComponent : Component
{
    [DataField("gridName")]
    public string? GridName;

    [DataField("localPosition")]
    public Vector2 LocalPosition;
}
