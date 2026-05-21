using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Player.Agg.Handlers;

/// <summary>
/// Handler for ReleaseFunds command.
/// </summary>
public static class ReleaseFundsHandler
{
    public static FundsReleased Handle(ReleaseFunds cmd, PlayerState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_FOUND", "Player does not exist");

        var tableKey = Convert.ToHexString(cmd.Key.ToByteArray()).ToLowerInvariant();
        if (
            !state.TableReservations.TryGetValue(tableKey, out var reservedForTable)
            || reservedForTable == 0
        )
            throw CommandRejectedError.PreconditionFailed(
                "NO_FUNDS_RESERVED_FOR_TABLE",
                "No funds reserved for this table");

        // Compute
        var newReserved = state.ReservedFunds - reservedForTable;
        var newAvailable = state.Bankroll - newReserved;
        return new FundsReleased
        {
            Amount = new Currency { Amount = reservedForTable, CurrencyCode = "CHIPS" },
            Key = cmd.Key,
            NewAvailableBalance = new Currency { Amount = newAvailable, CurrencyCode = "CHIPS" },
            NewReservedBalance = new Currency { Amount = newReserved, CurrencyCode = "CHIPS" },
            ReleasedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
