using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Player.Agg.Handlers;

/// <summary>
/// Handler for WithdrawFunds command.
/// </summary>
public static class WithdrawFundsHandler
{
    public static FundsWithdrawn Handle(WithdrawFunds cmd, PlayerState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_FOUND", "Player does not exist");

        var amount = cmd.Amount?.Amount ?? 0;
        if (amount <= 0)
            throw CommandRejectedError.InvalidArgument("AMOUNT_MUST_BE_POSITIVE", "amount must be positive");
        if (amount > state.AvailableBalance)
            throw CommandRejectedError.PreconditionFailed("INSUFFICIENT_AVAILABLE_BALANCE", "Insufficient funds");

        // Compute
        var newBalance = state.Bankroll - amount;
        return new FundsWithdrawn
        {
            Amount = cmd.Amount,
            NewBalance = new Currency { Amount = newBalance, CurrencyCode = "CHIPS" },
            WithdrawnAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
