using Content.Shared.MedicalRecords;
using Content.Shared.Access.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client.MedicalRecords.UI;

public sealed class MedicalRecordsConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    private readonly AccessReaderSystem _accessReader = default!;

    private MedicalRecordsConsoleWindow? _window;
    private string _filterText = "";
    private MedicalRecordsConsoleState? _lastState;

    public MedicalRecordsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _accessReader = EntMan.System<AccessReaderSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = new MedicalRecordsConsoleWindow(Owner, _playerManager, _accessReader)
        {
            Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName
        };

        _window.OnPatientSelected += patientName =>
        {
            SendMessage(new SelectMedicalRecord(patientName));
        };

        _window.OnRecordSaved += content =>
        {
            SendMessage(new SaveMedicalRecordMessage(content));
        };

        _window.OnFilterChanged += filter =>
        {
            _filterText = filter;
            if (_lastState != null)
            {
                _window?.UpdateState(_lastState, _filterText);
            }
        };

        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not MedicalRecordsConsoleState castState)
            return;

        _lastState = castState;
        _window?.UpdateState(castState, _filterText);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }
}
