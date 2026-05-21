using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Hand.SagaPlayer;

/// <summary>
/// Saga: Hand -> Player (OO Pattern)
///
/// Reacts to PotAwarded events from Hand domain.
/// Sends DepositFunds commands to Player domain.
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class HandPlayerSaga : Saga
{
    public override string Name => "saga-hand-player";
    public override string InputDomain => "hand";
    public override string OutputDomain => "player";

    [Handles(typeof(PotAwarded))]
    public List<CommandBook> HandlePotAwarded(PotAwarded evt)
    {
        var commands = new List<CommandBook>();

        foreach (var winner in evt.Winners)
        {
            var depositFunds = new DepositFunds
            {
                Amount = new Currency { Amount = winner.Amount, CurrencyCode = "CHIPS" },
            };

            var cmdAny = PackCommand(depositFunds);

            commands.Add(
                new CommandBook
                {
                    Cover = new Cover
                    {
                        Domain = "player",
                        Root = new UUID { Value = winner.PlayerRoot },
                    },
                    Pages =
                    {
                        new CommandPage
                        {
                            Header = new PageHeader
                            {
                                AngzarrDeferred = new AngzarrDeferredSequence(),
                            },
                            Command = cmdAny,
                        },
                    },
                }
            );
        }

        return commands;
    }
}
