using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class RebuyHandler
{
    public static IMessage Handle(ProcessRebuy cmd, TournamentState state)
    {
        if (!state.Exists)
            throw new CommandRejectedError("Tournament does not exist");
        if (!state.IsRunning)
            throw new CommandRejectedError("Tournament not running");

        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();
        if (!state.IsPlayerRegistered(rootHex))
            throw new CommandRejectedError("Player not registered");

        var config = state.RebuyConfig;
        if (config == null || !config.Enabled)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "rebuys_disabled",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        if (state.CurrentLevel > config.RebuyLevelCutoff)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "window_closed",
                DeniedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        var rebuysUsed = state.PlayerRebuyCount(rootHex);
        if (config.MaxRebuys > 0 && rebuysUsed >= config.MaxRebuys)
            return new RebuyDenied
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "max_reached",
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
