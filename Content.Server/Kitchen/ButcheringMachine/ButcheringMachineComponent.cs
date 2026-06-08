using Content.Shared.Chemistry.Components;
using Content.Shared.Storage;

namespace Content.Server.Kitchen.ButcheringMachine
{
    [RegisterComponent]
    public sealed partial class ButcheringMachineComponent : Component
    {
        [ViewVariables]
        public float RandomMessTimer = 0f;

        [ViewVariables(VVAccess.ReadWrite), DataField]
        public TimeSpan RandomMessInterval = TimeSpan.FromSeconds(5);

        [ViewVariables]
        public float ProcessingTimer = default;

        [ViewVariables]
        public Solution? BloodReagents = null;

        public List<EntitySpawnEntry> SpawnedEntities = new();

        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public float BaseInsertionDelay = 0.1f;

        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public float ProcessingTimePerUnitMass = 0.5f;

        [ViewVariables(VVAccess.ReadWrite), DataField]
        public bool SafetyEnabled = true;
    }
}
