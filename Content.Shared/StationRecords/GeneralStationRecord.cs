using Robust.Shared.Enums;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.StationRecords;

/// <summary>
///     General station record. Indicates the crewmember's name and job.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial record GeneralStationRecord
{
    /// <summary>
    ///     Name tied to this station record.
    /// </summary>
    [DataField("name")]
    public string Name = string.Empty;

    /// <summary>
    ///     Age of the person that this station record represents.
    /// </summary>
    [DataField("age")]
    public int Age;

    /// <summary>
    ///     Job title tied to this station record.
    /// </summary>
    [DataField("jobTitle")]
    public string JobTitle = string.Empty;

    /// <summary>
    ///     Job icon tied to this station record.
    /// </summary>
    [DataField("jobIcon")]
    public string JobIcon = string.Empty;

    [DataField("jobPrototype")]
    public string JobPrototype = string.Empty;

    /// <summary>
    ///     Species tied to this station record.
    /// </summary>
    [DataField("species")]
    public string Species = string.Empty;

    /// <summary>
    ///     Gender identity tied to this station record.
    /// </summary>
    /// <remarks>Sex should be placed in a medical record, not a general record.</remarks>
    [DataField("gender")]
    public Gender Gender = Gender.Epicene;

    /// <summary>
    ///     The priority to display this record at.
    ///     This is taken from the 'weight' of a job prototype,
    ///     usually.
    /// </summary>
    [DataField("displayPriority")]
    public int DisplayPriority;

    /// <summary>
    ///     Fingerprint of the person.
    /// </summary>
    [DataField("fingerprint")]
    public string? Fingerprint;

    /// <summary>
    ///     DNA of the person.
    /// </summary>
    [DataField("dna")]
    public string? DNA;
}
