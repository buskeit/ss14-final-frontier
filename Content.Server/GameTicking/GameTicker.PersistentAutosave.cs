using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;

namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        private void StartAutosaveLoop()
        {
            _autosaveLoopCts?.Cancel();
            _autosaveLoopCts = new CancellationTokenSource();
            var token = _autosaveLoopCts.Token;

            _ = Task.Run(async () =>
            {
                var lastTick = DateTime.UtcNow;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    var now = DateTime.UtcNow;
                    var elapsed = (float) (now - lastTick).TotalSeconds;
                    lastTick = now;

                    _taskManager.RunOnMainThread(() => UpdatePersistentAutosave(elapsed));
                }
            }, token);
        }

        public void LogAutosaveProbe(TimeSpan triggerAt, bool persistenceEnabledByProbe)
        {
            _ = Task.Run(async () =>
            {
                var delay = triggerAt - _gameTiming.CurTime;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);

                _sawmill.Info(
                    "AUTOSAVE PROBE: triggerAt={TriggerAt} persistenceEnabledByProbe={EnabledByProbe} {Status}",
                    triggerAt,
                    persistenceEnabledByProbe,
                    GetAutosaveStatus());
            });
        }

        private void UpdatePersistentAutosave(float frameTime)
        {
            var autosaveEnabled = _cfg.GetCVar(CCVars.AutoSaveEnabled);
            var usePersistence = _cfg.GetCVar(CCVars.UsePersistence);
            var intervalMinutes = _cfg.GetCVar(CCVars.AutoSaveInterval);
            var activeSource = RunLevel == GameRunLevel.InRound
                ? "in-round"
                : usePersistence ? "persistent" : null;

            _autosaveUpdateCount++;
            _lastAutosaveFrameTime = frameTime;
            _lastAutosaveSource = activeSource;

            if (!autosaveEnabled)
            {
                _lastAutosaveState = "autosave_disabled";
                LogAutosaveState(active: false, "autosave_disabled", intervalMinutes);
                return;
            }

            if (activeSource == null)
            {
                _lastAutosaveState = "persistence_disabled_outside_round";
                LogAutosaveState(active: false, "persistence_disabled_outside_round", intervalMinutes);
                return;
            }

            _lastAutosaveState = activeSource;
            LogAutosaveState(active: true, activeSource, intervalMinutes);

            RoundLengthMetric.Inc(frameTime);

            var interval = TimeSpan.FromMinutes(intervalMinutes);
            _timeToNextSave += TimeSpan.FromSeconds(frameTime);

            if (_warnings == 3 &&
                interval > TimeSpan.FromMinutes(5) &&
                _timeToNextSave > interval - TimeSpan.FromMinutes(5))
            {
                _warnings--;
                _sawmill.Info("Autosave warning threshold reached: source={Source} nextSave={NextSave} intervalMinutes={IntervalMinutes}",
                    activeSource, _timeToNextSave, intervalMinutes);
                SendServerMessage("The game will automatically save in 5 minutes.");
            }
            else if (_warnings == 2 &&
                     interval > TimeSpan.FromMinutes(1) &&
                     _timeToNextSave > interval - TimeSpan.FromMinutes(1))
            {
                _warnings--;
                _sawmill.Info("Autosave warning threshold reached: source={Source} nextSave={NextSave} intervalMinutes={IntervalMinutes}",
                    activeSource, _timeToNextSave, intervalMinutes);
                SendServerMessage("The game will automatically save in 1 minute.");
            }
            else if (_warnings == 1 &&
                     _timeToNextSave > interval - TimeSpan.FromSeconds(3))
            {
                _warnings--;
                _sawmill.Info("Autosave save threshold imminent: source={Source} nextSave={NextSave} intervalMinutes={IntervalMinutes}",
                    activeSource, _timeToNextSave, intervalMinutes);
                SendServerMessage("The game is saving..");
            }

            if (_timeToNextSave <= interval)
                return;

            _sawmill.Info(
                "Autosave threshold reached: source={Source} runLevel={RunLevel} autosaveenabled={AutoSaveEnabled} usepersistence={UsePersistence} intervalMinutes={IntervalMinutes} nextSave={NextSave}",
                activeSource, RunLevel, autosaveEnabled, usePersistence, intervalMinutes, _timeToNextSave);

            _timeToNextSave = TimeSpan.Zero;
            _warnings = 3;

            if (SaveMaps(activeSource, out var result))
            {
                SendServerMessage("Game Saved.");
                return;
            }

            _sawmill.Error("Autosave save attempt failed: {Result}", result);
        }

        private void LogAutosaveState(bool active, string state, int intervalMinutes)
        {
            var stateKey =
                $"{active}|{state}|{RunLevel}|{_cfg.GetCVar(CCVars.AutoSaveEnabled)}|{_cfg.GetCVar(CCVars.UsePersistence)}|{intervalMinutes}";

            if (_lastAutosaveStateKey == stateKey)
                return;

            _lastAutosaveStateKey = stateKey;

            _sawmill.Info(
                "Autosave state changed: active={Active} state={State} runLevel={RunLevel} autosaveenabled={AutoSaveEnabled} usepersistence={UsePersistence} intervalMinutes={IntervalMinutes} nextSave={NextSave}",
                active, state, RunLevel, _cfg.GetCVar(CCVars.AutoSaveEnabled), _cfg.GetCVar(CCVars.UsePersistence),
                intervalMinutes, _timeToNextSave);
        }
    }
}
