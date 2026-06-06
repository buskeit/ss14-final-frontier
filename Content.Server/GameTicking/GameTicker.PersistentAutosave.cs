using Content.Shared.CCVar;

namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        private void UpdatePersistentAutosave(float frameTime)
        {
            if (!_cfg.GetCVar(CCVars.AutoSaveEnabled) ||
                !_cfg.GetCVar(CCVars.UsePersistence) ||
                RunLevel == GameRunLevel.InRound)
            {
                return;
            }

            RoundLengthMetric.Inc(frameTime);

            _timeToNextSave += TimeSpan.FromSeconds(frameTime);
            if (_warnings == 3)
            {
                if (_timeToNextSave > TimeSpan.FromMinutes(_cfg.GetCVar(CCVars.AutoSaveInterval) - 5))
                {
                    _warnings--;
                    SendServerMessage("The game will automatically save in 5 minutes.");
                }
            }
            else if (_warnings == 2)
            {
                if (_timeToNextSave > TimeSpan.FromMinutes(_cfg.GetCVar(CCVars.AutoSaveInterval) - 1))
                {
                    _warnings--;
                    SendServerMessage("The game will automatically save in 1 minute.");
                }
            }
            else if (_warnings == 1)
            {
                if (_timeToNextSave > TimeSpan.FromMinutes(_cfg.GetCVar(CCVars.AutoSaveInterval)) - TimeSpan.FromSeconds(3))
                {
                    _warnings--;
                    SendServerMessage("The game is saving..");
                }
            }

            if (_timeToNextSave <= TimeSpan.FromMinutes(_cfg.GetCVar(CCVars.AutoSaveInterval)))
                return;

            _timeToNextSave = TimeSpan.Zero;
            _warnings = 3;
            SaveMaps();
            SendServerMessage("Game Saved.");
        }
    }
}
