using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System.Globalization;

namespace Content.Shared.Security.Components;

/// <summary>
/// This is used for a locker that automatically sets up and handles a <see cref="GenpopIdCardComponent"/>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GenpopLockerComponent : Component
{
    public const int MaxCrimeLength = 48;
    public const int MaxSentenceDays = 99_999;

    public static bool TryParseSentenceDays(string? value, out int days, out TimeSpan duration)
    {
        days = 0;
        duration = TimeSpan.Zero;

        return !string.IsNullOrWhiteSpace(value) &&
               int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out days) &&
               TryConvertSentenceDays(days, out duration);
    }

    public static bool TryConvertSentenceDays(int days, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (days < 0 || days > MaxSentenceDays)
            return false;

        duration = TimeSpan.FromDays(days);
        return true;
    }

    /// <summary>
    /// The <see cref="GenpopIdCardComponent"/> that this locker is currently associated with.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? LinkedId;

    /// <summary>
    /// The Prototype spawned.
    /// </summary>
    [DataField]
    public EntProtoId<GenpopIdCardComponent> IdCardProto = "PrisonerIDCard";
}

[Serializable, NetSerializable]
public sealed class GenpopLockerIdConfiguredMessage : BoundUserInterfaceMessage
{
    public string Name;
    public int SentenceDays;
    public string Crime;

    public GenpopLockerIdConfiguredMessage(string name, int sentenceDays, string crime)
    {
        Name = name;
        SentenceDays = sentenceDays;
        Crime = crime;
    }
}

[Serializable, NetSerializable]
public enum GenpopLockerUiKey : byte
{
    Key
}
