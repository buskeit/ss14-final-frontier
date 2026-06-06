using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Security;

[Prototype("spaceLawCrime")]
public sealed partial class SpaceLawCrimePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = default!;

    [DataField(required: true)]
    public string Category = default!;

    [DataField]
    public int BrigTime;

    [DataField]
    public int Fine;

    [DataField]
    public int Order;
}
