using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client.Launcher
{
    public sealed class LauncherConnecting : Robust.Client.State.State
    {
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly IClientNetManager _clientNetManager = default!;
        [Dependency] private readonly IGameController _gameController = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IClipboardManager _clipboard = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private LauncherConnectingGui? _control;
        private ISawmill _sawmill = default!;

        private Page _currentPage;
        private string? _connectFailReason;

        public string? Address => _gameController.LaunchState.Ss14Address ?? _gameController.LaunchState.ConnectAddress;

        public string? ConnectFailReason
        {
            get => _connectFailReason;
            private set
            {
                _connectFailReason = value;
                ConnectFailReasonChanged?.Invoke(value);
            }
        }

        public string? LastDisconnectReason => _baseClient.LastDisconnectReason;

        public Page CurrentPage
        {
            get => _currentPage;
            private set
            {
                _currentPage = value;
                PageChanged?.Invoke(value);
            }
        }

        public ClientConnectionState ConnectionState => _clientNetManager.ClientConnectState;

        public event Action<Page>? PageChanged;
        public event Action<string?>? ConnectFailReasonChanged;
        public event Action<ClientConnectionState>? ConnectionStateChanged;
        public event Action<NetConnectFailArgs>? ConnectFailed;

        protected override void Startup()
        {
            _sawmill = _logManager.GetSawmill("launcher-flow");
            _control = new LauncherConnectingGui(this, _random, _prototypeManager, _cfg, _clipboard);

            _sawmill.Info(
                $"Launcher client flow started: fork={_cfg.GetCVar(CVars.BuildForkId)}, " +
                $"version={_cfg.GetCVar(CVars.BuildVersion)}, engine={_cfg.GetCVar(CVars.BuildEngineVersion)}, " +
                $"targetConfigured={!string.IsNullOrWhiteSpace(_gameController.LaunchState.ConnectAddress)}, " +
                $"ss14AddressConfigured={!string.IsNullOrWhiteSpace(_gameController.LaunchState.Ss14Address)}.");

            _userInterfaceManager.StateRoot.AddChild(_control);

            _clientNetManager.ConnectFailed += OnConnectFailed;
            _clientNetManager.ClientConnectStateChanged += OnConnectStateChanged;

            CurrentPage = Page.Connecting;
        }

        protected override void Shutdown()
        {
            _control?.Dispose();

            _clientNetManager.ConnectFailed -= OnConnectFailed;
            _clientNetManager.ClientConnectStateChanged -= OnConnectStateChanged;
        }

        private void OnConnectFailed(object? _, NetConnectFailArgs args)
        {
            _sawmill.Warning(
                $"Launcher connection failed: state={_clientNetManager.ClientConnectState}, " +
                $"redialRequested={args.RedialFlag}, reason={args.Reason}");

            if (args.RedialFlag)
            {
                // We've just *attempted* to connect and we've been told we need to redial, so do it.
                // Result deliberately discarded.
                Redial();
            }
            ConnectFailReason = args.Reason;
            CurrentPage = Page.ConnectFailed;
            ConnectFailed?.Invoke(args);
        }

        private void OnConnectStateChanged(ClientConnectionState state)
        {
            _sawmill.Info($"Launcher connection state changed: state={state}.");
            ConnectionStateChanged?.Invoke(state);
        }

        public void RetryConnect()
        {
            if (_gameController.LaunchState.ConnectEndpoint != null)
            {
                _sawmill.Info("Retrying launcher connection with the configured endpoint.");
                _baseClient.ConnectToServer(_gameController.LaunchState.ConnectEndpoint);
                CurrentPage = Page.Connecting;
                return;
            }

            _sawmill.Warning("Launcher connection retry rejected: no configured endpoint.");
        }

        public bool Redial()
        {
            try
            {
                if (_gameController.LaunchState.Ss14Address != null)
                {
                    _sawmill.Info("Launcher redial requested using the configured SS14 address.");
                    _gameController.Redial(_gameController.LaunchState.Ss14Address);
                    return true;
                }
                else
                {
                    _sawmill.Info($"Redial not possible, no Ss14Address");
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Redial exception: {ex}");
            }
            return false;
        }

        public void Exit()
        {
            _gameController.Shutdown("Exit button pressed");
        }

        public void SetDisconnected()
        {
            _sawmill.Warning(
                $"Launcher client disconnected: state={_clientNetManager.ClientConnectState}, " +
                $"reason={LastDisconnectReason ?? "not-provided"}.");
            CurrentPage = Page.Disconnected;
        }

        public enum Page : byte
        {
            Connecting,
            ConnectFailed,
            Disconnected,
        }
    }
}
