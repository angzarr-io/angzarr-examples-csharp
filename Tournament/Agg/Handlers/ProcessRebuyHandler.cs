using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class ProcessRebuyHandler
{
    public static IMessage Handle(ProcessRebuy cmd, TournamentState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_FOUND",
                "Tournament does not exist");
        if (!state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_RUNNING",
                "Tournament is not running");

        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();
        // EU-0818: unregistered players get RebuyDenied as a domain event so
        // the PM sees the outcome on the stream (not a raised error).
        if (!state.IsPlayerRegistered(rootHex))
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Player not registered",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        var config = state.RebuyConfig;
        if (config == null || !config.Enabled)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Rebuys are not enabled",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        // cutoff_level = 0 disables the level-cutoff check entirely (EU-0840).
        if (config.RebuyLevelCutoff > 0 && state.CurrentLevel > config.RebuyLevelCutoff)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Rebuy window closed",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        var rebuysUsed = state.PlayerRebuyCount(rootHex);
        // max_rebuys = 0 → unlimited (EU-0841).
        if (config.MaxRebuys > 0 && rebuysUsed >= config.MaxRebuys)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Maximum rebuys reached",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        return new RebuyProcessed
        {
            PlayerRoot = cmd.PlayerRoot,
            ReservationId = cmd.ReservationId,
            RebuyCost = config.RebuyCost,
            ChipsAdded = config.RebuyChips,
            RebuyCount = rebuysUsed + 1,
            ProcessedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
