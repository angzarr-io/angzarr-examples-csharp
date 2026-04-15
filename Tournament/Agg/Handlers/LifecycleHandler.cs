using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class LifecycleHandler
{
    public static BlindLevelAdvanced HandleAdvanceBlind(AdvanceBlindLevel cmd, TournamentState state)
    {
        if (!state.IsRunning)
            throw new CommandRejectedError("Tournament not running");

        var next = state.CurrentLevel + 1;
        long sb = 0, bb = 0, ante = 0;
        if (state.BlindStructure.Count > 0)
        {
            var idx = Math.Min(next - 1, state.BlindStructure.Count - 1);
            if (idx >= 0)
            {
                sb = state.BlindStructure[idx].SmallBlind;
                bb = state.BlindStructure[idx].BigBlind;
                ante = state.BlindStructure[idx].Ante;
            }
        }

        return new BlindLevelAdvanced
        {
            Level = next,
            SmallBlind = sb,
            BigBlind = bb,
            Ante = ante,
            AdvancedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static PlayerEliminated HandleEliminate(EliminatePlayer cmd, TournamentState state)
    {
        if (!state.IsRunning)
            throw new CommandRejectedError("Tournament not running");

        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();
        if (!state.IsPlayerRegistered(rootHex))
            throw new CommandRejectedError("Player not registered");

        return new PlayerEliminated
        {
            PlayerRoot = cmd.PlayerRoot,
            FinishPosition = state.PlayersRemaining,
            HandRoot = cmd.HandRoot,
            EliminatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static TournamentPaused HandlePause(PauseTournament cmd, TournamentState state)
    {
        if (!state.IsRunning)
            throw new CommandRejectedError("Tournament not running");

        return new TournamentPaused
        {
            Reason = cmd.Reason,
            PausedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static TournamentResumed HandleResume(ResumeTournament cmd, TournamentState state)
    {
        if (state.Status != TournamentStatus.TournamentPaused)
            throw new CommandRejectedError("Tournament not paused");

        return new TournamentResumed
        {
            ResumedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
