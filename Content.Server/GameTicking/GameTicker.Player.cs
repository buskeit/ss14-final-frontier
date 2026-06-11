using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.GameWindow;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking
{
    [UsedImplicitly]
    public sealed partial class GameTicker
    {
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        private readonly HashSet<NetUserId> _pendingCharacterSetup = new();

        private void InitializePlayer()
        {
            _playerManager.PlayerStatusChanged += PlayerStatusChanged;
        }

        private ContentPlayerData EnsureContentData(ICommonSession session, EntityUid? mindId = null)
        {
            if (session.Data.ContentDataUncast is ContentPlayerData data)
            {
                if (mindId != null)
                    data.Mind = mindId;

                return data;
            }

            data = new ContentPlayerData(session.UserId, session.Name)
            {
                Mind = mindId
            };
            session.Data.ContentDataUncast = data;
            return data;
        }

        public bool TryRejoin(ICommonSession session)
        {
            EntityUid? mindId = null;
            MindComponent? mind = null;
            EntityUid? body = null;
            if (!_mind.TryGetMind(session.UserId, out mindId, out mind)) return false;
            _pvsOverride.AddSessionOverride(mindId.Value, session);
            string charactername = "";
            if (mind != null)
            {
                if (mind.CharacterName != null)
                    charactername = mind.CharacterName;
                if (mind.OwnedEntity != null && mind.OwnedEntity != EntityUid.Invalid) body = mind.OwnedEntity;


                else if (mind.CurrentEntity != null && mind.CurrentEntity != EntityUid.Invalid) body = mind.CurrentEntity;

                else mindId = null;
            }
            if (session.GetMind() != mindId && body != null && body != EntityUid.Invalid)
            {
                PlayerJoinGame(session, true);
                var station = EntityUid.Invalid;
                //   _mind.SetUserId((EntityUid)mindId!, session.UserId, mind);
                _mind.WipeMind(body);
                var newMind = _mind.CreateMind(session.UserId, mind!.CharacterName);
                _mind.SetUserId(newMind, session.UserId);
                _mind.TransferTo(newMind, body);
                _playerManager.SetAttachedEntity(session, body, true);
                // We raise this event directed to the mob, but also broadcast it so game rules can do something now.
                PlayersJoinedRoundNormally++;
                HumanoidCharacterProfile? character = GetPlayerProfile(session);
                if (character == null) character = new HumanoidCharacterProfile();
                var aev = new PlayerSpawnCompleteEvent(body.Value,
                    session,
                    "Passenger",
                    true,
                    false,
                    PlayersJoinedRoundNormally,
                    station,
                    character);
                RaiseLocalEvent(body.Value, aev, true);
                return true;
            }
            return false;
        }
        private async void PlayerStatusChanged(object? sender, SessionStatusEventArgs args)
        {
            EntityUid? mindId = null;
            MindComponent? mind = null;
            var session = args.Session;
            EntityUid? body = null;
            if (args.NewStatus != SessionStatus.Disconnected && _mind.TryGetMind(session.UserId, out mindId, out mind))
            {
                if (args.NewStatus != SessionStatus.Disconnected)
                {
                    _pvsOverride.AddSessionOverride(mindId.Value, session);
                }
            }
            if (mind != null)
            {
                if (mind.OwnedEntity != null && mind.OwnedEntity != EntityUid.Invalid) body = mind.OwnedEntity;


                else if (mind.CurrentEntity != null && mind.CurrentEntity != EntityUid.Invalid) body = mind.CurrentEntity;

                else mindId = null;
            }
            if (session.GetMind() != mindId && body != null && body != EntityUid.Invalid)
            {
                EnsureContentData(session, mindId);
                //   _mind.SetUserId((EntityUid)mindId!, session.UserId, mind);
                var newMind = _mind.CreateMind(session.UserId, mind!.CharacterName);
                _mind.SetUserId(newMind, session.UserId);
                _mind.TransferTo(newMind, body);
                _playerManager.SetAttachedEntity(session, body, true);
            }

            //  DebugTools.Assert(session.GetMind() == mindId);

            switch (args.NewStatus)
            {
                case SessionStatus.Connected:
                    {
                        _launcherFlowSawmill.Info(
                            $"Connection accepted: userId={session.UserId}, authType={session.AuthType}, " +
                            $"lobbyBypass={PersistentJoinEnabled}, lobbyEnabled={LobbyEnabled}, " +
                            $"serverFork={_cfg.GetCVar(CVars.BuildForkId)}, " +
                            $"serverVersion={_cfg.GetCVar(CVars.BuildVersion)}, " +
                            $"serverEngine={_cfg.GetCVar(CVars.BuildEngineVersion)}.");

                        AddPlayerToDb(args.Session.UserId.UserId);

                        // Always make sure the client has player data.
                        EnsureContentData(session, mindId);

                        // Make the player actually join the game.
                        // timer time must be > tick length
                        Timer.Spawn(0, () => _playerManager.JoinGame(args.Session));

                        var record = await _db.GetPlayerRecordByUserId(args.Session.UserId);
                        var firstConnection = record != null &&
                                              Math.Abs((record.FirstSeenTime - record.LastSeenTime).TotalMinutes) < 1;

                        _chatManager.SendAdminAnnouncement(firstConnection
                            ? Loc.GetString("player-first-join-message", ("name", args.Session.Name))
                            : Loc.GetString("player-join-message", ("name", args.Session.Name)));

                        RaiseNetworkEvent(GetConnectionStatusMsg(), session.Channel);

                        if (firstConnection && _cfg.GetCVar(CCVars.AdminNewPlayerJoinSound))
                            _audio.PlayGlobal(new SoundPathSpecifier("/Audio/Effects/newplayerping.ogg"),
                                Filter.Empty().AddPlayers(_adminManager.ActiveAdmins), false,
                                audioParams: new AudioParams { Volume = -5f });

                        if (LobbyEnabled && _roundStartCountdownHasNotStartedYetDueToNoPlayers)
                        {
                            _roundStartCountdownHasNotStartedYetDueToNoPlayers = false;
                            _roundStartTime = _gameTiming.CurTime + LobbyDuration;
                        }

                        break;
                    }

                case SessionStatus.InGame:
                    {
                        EnsureContentData(session, mindId);
                        _userDb.ClientConnected(session);

                        if (mind == null)
                        {
                            if (PersistentJoinEnabled)
                            {
                                _launcherFlowSawmill.Info(
                                    $"Waiting for profile load before persistent routing: userId={session.UserId}.");
                                PersistentJoinWaitDb();
                            }
                            else if (LobbyEnabled)
                            {
                                _launcherFlowSawmill.Info(
                                    $"Routing player to standard lobby: userId={session.UserId}, reason=lobby-enabled, lobbyBypass=false.");
                                PlayerJoinLobby(session);
                            }
                            else
                            {
                                _launcherFlowSawmill.Info(
                                    $"Routing player to direct spawn: userId={session.UserId}, reason=lobby-disabled, lobbyBypass=false.");
                                SpawnWaitDb();
                            }

                            _adminLogger.Add(LogType.Connection, LogImpact.Low, $"User {args.Session:Player} attached to {(args.Session.AttachedEntity != null ? ToPrettyString(args.Session.AttachedEntity) : "nothing"):entity} connected to the game.");
                            break;
                        }

                        if (mind.CurrentEntity == null || Deleted(mind.CurrentEntity))
                        {
                            DebugTools.Assert(mind.CurrentEntity == null, "a mind's current entity was deleted without updating the mind");

                            // This player is joining the game with an existing mind, but the mind has no entity.
                            // Their entity was probably deleted sometime while they were disconnected, or they were an observer.
                            // Instead of allowing them to spawn in, we will dump and their existing mind in an observer ghost.
                            if (PersistentJoinEnabled)
                                PersistentJoinWaitDb();
                            else
                                PlayerJoinLobby(session);
                        }
                        else
                        {
                            if (_playerManager.SetAttachedEntity(session, mind.CurrentEntity, true))
                            {
                                PlayerJoinGame(session);
                            }
                            else
                            {
                                Log.Error(
                                    $"Failed to attach player {session} with mind {ToPrettyString(mindId)} to its current entity {ToPrettyString(mind.CurrentEntity)}");
                                SpawnObserverWaitDb();
                            }
                        }

                        _adminLogger.Add(LogType.Connection, LogImpact.Low, $"User {args.Session:Player} attached to {(args.Session.AttachedEntity != null ? ToPrettyString(args.Session.AttachedEntity) : "nothing"):entity} connected to the game.");

                        break;
                    }

                case SessionStatus.Disconnected:
                    {
                        if (_pendingCharacterSetup.Remove(session.UserId))
                        {
                            _launcherFlowSawmill.Warning(
                                $"Disconnected before character setup completed: userId={session.UserId}, " +
                                $"oldStatus={args.OldStatus}, attachedEntity={session.AttachedEntity != null}.");
                        }
                        else
                        {
                            _launcherFlowSawmill.Info(
                                $"Session disconnected: userId={session.UserId}, oldStatus={args.OldStatus}, " +
                                $"attachedEntity={session.AttachedEntity != null}.");
                        }

                        _chatManager.SendAdminAnnouncement(Loc.GetString("player-leave-message", ("name", args.Session.Name)));
                        if (mindId != null)
                        {
                            _pvsOverride.RemoveSessionOverride(mindId.Value, session);
                        }

                        _userDb.ClientDisconnected(session);

                        _adminLogger.Add(LogType.Connection, LogImpact.Low, $"User {args.Session:Player} attached to {(args.Session.AttachedEntity != null ? ToPrettyString(args.Session.AttachedEntity) : "nothing"):entity} disconnected from the game.");
                        break;
                    }
            }



            //When the status of a player changes, update the server info text
            UpdateInfoText();

            async void SpawnWaitDb()
            {
                try
                {
                    await _userDb.WaitLoadComplete(session);
                }
                catch (OperationCanceledException)
                {
                    // Bail, user must've disconnected or something.
                    Log.Debug($"Database load cancelled while waiting to spawn {session}");
                    return;
                }

                SpawnPlayer(session, EntityUid.Invalid);
            }

            async void SpawnObserverWaitDb()
            {
                try
                {
                    await _userDb.WaitLoadComplete(session);
                }
                catch (OperationCanceledException)
                {
                    // Bail, user must've disconnected or something.
                    Log.Debug($"Database load cancelled while waiting to spawn {session}");
                    return;
                }

                JoinAsObserver(session);
            }

            async void PersistentJoinWaitDb()
            {
                try
                {
                    await _userDb.WaitLoadComplete(session);
                }
                catch (OperationCanceledException)
                {
                    _launcherFlowSawmill.Warning(
                        $"Profile load cancelled before persistent routing completed: userId={session.UserId}, " +
                        $"sessionStatus={session.Status}.");
                    return;
                }

                _launcherFlowSawmill.Info(
                    $"Profile load completed; evaluating persistent route: userId={session.UserId}.");
                JoinPersistentPlayer(session);
            }

            async void AddPlayerToDb(Guid id)
            {
                if (RoundId != 0 && _runLevel != GameRunLevel.PreRoundLobby)
                {
                    await _db.AddRoundPlayers(RoundId, id);
                }
            }
        }

        public HumanoidCharacterProfile? GetPlayerProfile(ICommonSession p)
        {
            var preferences = _prefsManager.GetPreferences(p.UserId);
            if (preferences.SelectedCharacter is { } selected)
                return selected;

            if (PersistentJoinEnabled)
                return null;

            foreach (var character in preferences.Characters.Values)
                return character;

            return HumanoidCharacterProfile.Random();
        }

        private HumanoidCharacterProfile? GetPersistentSelectedProfile(ICommonSession p)
        {
            var selected = _prefsManager.GetPreferences(p.UserId).SelectedCharacter;
            if (selected == null)
                return null;

            if (IsUnfinalizedPersistentProfile(selected))
                return null;

            return selected;
        }

        private static bool IsUnfinalizedPersistentProfile(HumanoidCharacterProfile profile)
        {
            var defaultProfile = new HumanoidCharacterProfile();
            return profile.Name == defaultProfile.Name &&
                   profile.Species == defaultProfile.Species &&
                   profile.Age == defaultProfile.Age &&
                   profile.Sex == defaultProfile.Sex &&
                   profile.Gender == defaultProfile.Gender &&
                   profile.FlavorText == defaultProfile.FlavorText;
        }

        private bool PersistentJoinEnabled => _cfg.GetCVar(CCVars.UsePersistence);

        private void JoinPersistentPlayer(ICommonSession session)
        {
            var preferences = _prefsManager.GetPreferences(session.UserId);
            var selected = preferences.SelectedCharacter;
            var finalized = selected != null && !IsUnfinalizedPersistentProfile(selected);

            if (finalized)
            {
                _launcherFlowSawmill.Info(
                    $"Persistent route selected: userId={session.UserId}, route=spawn, " +
                    $"reason=finalized-selected-character, characterCount={preferences.Characters.Count}, " +
                    $"selectedSlot={preferences.SelectedCharacterIndex}, lobbyBypass=true.");
                MakeJoinGamePersistent(session);
                return;
            }

            var reason = preferences.Characters.Count == 0
                ? "no-characters"
                : selected == null
                    ? "selected-character-missing"
                    : "selected-character-unfinalized";

            _launcherFlowSawmill.Info(
                $"Persistent route selected: userId={session.UserId}, route=character-setup, " +
                $"reason={reason}, characterCount={preferences.Characters.Count}, " +
                $"selectedSlot={preferences.SelectedCharacterIndex}, lobbyBypass=true.");
            PlayerJoinLobby(session, forceCharacterSetup: true);
        }

        public void PlayerJoinGame(ICommonSession session, bool silent = false)
        {
            var completedSetup = _pendingCharacterSetup.Remove(session.UserId);
            _launcherFlowSawmill.Info(
                $"Gameplay join event requested: userId={session.UserId}, " +
                $"characterSetupCompleted={completedSetup}, attachedEntity={session.AttachedEntity != null}.");

            if (!silent)
                _chatManager.DispatchServerMessage(session, Loc.GetString("game-ticker-player-join-game-message"));

            _playerGameStatuses[session.UserId] = PlayerGameStatus.JoinedGame;
            _db.AddRoundPlayers(RoundId, session.UserId);

            if (_adminManager.HasAdminFlag(session, AdminFlags.Admin))
            {
                if (_allPreviousGameRules.Count > 0)
                {
                    var rulesMessage = GetGameRulesListMessage(true);
                    _chatManager.SendAdminAnnouncementMessage(session, Loc.GetString("starting-rule-selected-preset", ("preset", rulesMessage)));
                }
            }

            RaiseNetworkEvent(new TickerJoinGameEvent(), session.Channel);
        }

        public void PlayerJoinLobby(ICommonSession session, bool forceCharacterSetup = false)
        {
            if (session == null) return;
            var persistentMode = PersistentJoinEnabled;
            if (forceCharacterSetup)
                _pendingCharacterSetup.Add(session.UserId);

            _playerGameStatuses[session.UserId] = persistentMode
                ? forceCharacterSetup
                    ? PlayerGameStatus.NotReadyToPlay
                    : PlayerGameStatus.ReadyToPlay
                : LobbyEnabled
                    ? PlayerGameStatus.NotReadyToPlay
                    : PlayerGameStatus.ReadyToPlay;
            _db.AddRoundPlayers(RoundId, session.UserId);

            var client = session.Channel;
            _launcherFlowSawmill.Info(
                $"Lobby transition requested: userId={session.UserId}, persistentMode={persistentMode}, " +
                $"lobbyBypass={persistentMode}, forceCharacterSetup={forceCharacterSetup}, " +
                $"playerStatus={_playerGameStatuses[session.UserId]}.");
            RaiseNetworkEvent(new TickerJoinLobbyEvent(persistentMode, forceCharacterSetup), client);
            RaiseNetworkEvent(GetStatusMsg(session), client);
            RaiseNetworkEvent(GetInfoMsg(), client);
            RaiseLocalEvent(new PlayerJoinedLobbyEvent(session));
        }

        private void ReqWindowAttentionAll()
        {
            RaiseNetworkEvent(new RequestWindowAttentionEvent());
        }
    }

    public sealed class PlayerJoinedLobbyEvent : EntityEventArgs
    {
        public readonly ICommonSession PlayerSession;

        public PlayerJoinedLobbyEvent(ICommonSession playerSession)
        {
            PlayerSession = playerSession;
        }
    }
}
