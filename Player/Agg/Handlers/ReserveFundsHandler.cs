// DOC: This file is referenced in docs/docs/examples/aggregates.mdx
//      Update documentation when making changes to handler patterns.

using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Player.Agg.Handlers;

// docs:start:reserve_funds_imp
/// <summary>
/// Handler for ReserveFunds command.
/// </summary>
public static class ReserveFundsHandler
{
    public static FundsReserved Handle(ReserveFunds cmd, PlayerState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_FOUND", "Player does not exist");

        var amount = cmd.Amount?.Amount ?? 0;
        if (amount <= 0)
            throw CommandRejectedError.InvalidArgument("AMOUNT_MUST_BE_POSITIVE", "amount must be positive");

        var tableKey = Convert.ToHexString(cmd.Key.ToByteArray()).ToLowerInvariant();
        if (state.TableReservations.ContainsKey(tableKey))
            throw CommandRejectedError.PreconditionFailed(
                "FUNDS_ALREADY_RESERVED_FOR_TABLE",
                "Funds already reserved for this table");
        if (amount > state.AvailableBalance)
            throw CommandRejectedError.PreconditionFailed(
                "INSUFFICIENT_FUNDS",
                "Insufficient funds");

        // Compute
        var newReserved = state.ReservedFunds + amount;
        var newAvailable = state.Bankroll - newReserved;
        return new FundsReserved
        {
            Amount = cmd.Amount,
            Key = cmd.Key,
            NewAvailableBalance = new Currency { Amount = newAvailable, CurrencyCode = "CHIPS" },
            NewReservedBalance = new Currency { Amount = newReserved, CurrencyCode = "CHIPS" },
            ReservedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
// docs:end:reserve_funds_imp
