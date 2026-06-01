using Content.Shared.CrewAssignments.Components;
using Content.Shared.CrewAssignments.Prototypes;
using Content.Shared.CrewAssignments.Systems;
using Content.Shared.Precursor;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CrewAssignments;

[Serializable, NetSerializable]
public enum JobNetUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class JobNetUpdateState : BoundUserInterfaceState
{
    public Dictionary<int, string>? Stations { get; set; }
    public string? AssignmentName;
    public int? Wage;
    public int SelectedStation;
    public TimeSpan? RemainingMinutes;
    public List<WorldObjectivesEntry> CurrentObjectives;
    public List<WorldObjectivesEntry> CompletedObjectives;
    public List<CodexEntry> CodexEntries;
    public ProtoId<NetworkLevelPrototype> Level;
    public int Balance;
    public bool SpendAuth;
    public int Spent;
    public int Spendable;
    public int Precursor;
    public List<ProtoId<PrecursorObjectivePrototype>> Objectives;
    public TimeSpan PrecursorResetTime;
    public ProtoId<RogueLevelPrototype> RogueLevel;
    public int XP;
    public RogueNetworkType NetworkType;
    public JobNetUpdateState(Dictionary<int, string>? stations, string? assignmentName, int? wage, int selectedStation, TimeSpan? remainingMinutes, List<WorldObjectivesEntry> currentObjectives, List<WorldObjectivesEntry> completedObjectives, List<CodexEntry> codexEntries, ProtoId<NetworkLevelPrototype> level, int balance, bool spendAuth, int spent, int spendable, int precursor, List<ProtoId<PrecursorObjectivePrototype>> objectives, TimeSpan precursorResetTime, ProtoId<RogueLevelPrototype> rogueLevel, int xP, RogueNetworkType networkType)
    {
        Stations = stations;
        AssignmentName = assignmentName;
        Wage = wage;
        SelectedStation = selectedStation;
        RemainingMinutes = remainingMinutes;
        CurrentObjectives = currentObjectives;
        CompletedObjectives = completedObjectives;
        CodexEntries = codexEntries;
        Level = level;
        Balance = balance;
        SpendAuth = spendAuth;
        Spent = spent;
        Spendable = spendable;
        Precursor = precursor;
        Objectives = objectives;
        PrecursorResetTime = precursorResetTime;
        RogueLevel = rogueLevel;
        XP = xP;
        NetworkType = networkType;
    }
}

[Serializable, NetSerializable]
public sealed class JobNetRequestUpdateInterfaceMessage : BoundUserInterfaceMessage
{

}

[Serializable, NetSerializable]
public sealed class JobNetSelectMessage : BoundUserInterfaceMessage
{
    public int ID;
    public JobNetSelectMessage(int id)
    {
        ID = id;
    }
}


[Serializable, NetSerializable]
public sealed class JobNetPurchaseMessage : BoundUserInterfaceMessage
{
    public JobNetPurchaseMessage()
    {
    }
}


[Serializable, NetSerializable]
public sealed class JobNetSelectRogueNetMessage : BoundUserInterfaceMessage
{
    public RogueNetworkType Net;
    public JobNetSelectRogueNetMessage(RogueNetworkType net)
    {
        Net = net;
    }
}
