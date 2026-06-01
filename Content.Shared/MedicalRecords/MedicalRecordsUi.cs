using Robust.Shared.Serialization;

namespace Content.Shared.MedicalRecords;

[Serializable, NetSerializable]
public enum MedicalRecordsConsoleKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class MedicalRecordsConsoleState : BoundUserInterfaceState
{
    public readonly string? SelectedPatient;
    public readonly string? PatientJob;
    public readonly string? MedicalRecord;
    public readonly Dictionary<string, string>? PatientListing;

    public MedicalRecordsConsoleState(string? selectedPatient, string? patientJob, string? medicalRecord, Dictionary<string, string>? patientListing)
    {
        SelectedPatient = selectedPatient;
        PatientJob = patientJob;
        MedicalRecord = medicalRecord;
        PatientListing = patientListing;
    }
}

[Serializable, NetSerializable]
public sealed class SelectMedicalRecord : BoundUserInterfaceMessage
{
    public readonly string? PatientName;

    public SelectMedicalRecord(string? patientName)
    {
        PatientName = patientName;
    }
}

[Serializable, NetSerializable]
public sealed class SaveMedicalRecordMessage : BoundUserInterfaceMessage
{
    public readonly string Content;

    public SaveMedicalRecordMessage(string content)
    {
        Content = content;
    }
}
