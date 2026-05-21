using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Player.Agg.Handlers;

namespace Player.Agg;

/// <summary>
/// Functional router for Player aggregate.
/// </summary>
public static class PlayerRouter
{
    // docs:start:command_router
    public static CommandRouter Create()
    {
        return new CommandRouter("player", eb => PlayerState.FromEventBook(eb))
            .On<RegisterPlayer>((cmd, state) => RegisterPlayerHandler.Handle(cmd, (PlayerState)state))
            .On<DepositFunds>((cmd, state) => DepositFundsHandler.Handle(cmd, (PlayerState)state))
            .On<WithdrawFunds>((cmd, state) => WithdrawFundsHandler.Handle(cmd, (PlayerState)state))
            .On<ReserveFunds>((cmd, state) => ReserveFundsHandler.Handle(cmd, (PlayerState)state))
            .On<ReleaseFunds>((cmd, state) => ReleaseFundsHandler.Handle(cmd, (PlayerState)state))
            .On<TransferFunds>((cmd, state) => TransferFundsHandler.Handle(cmd, (PlayerState)state));
    }
    // docs:end:command_router
}
