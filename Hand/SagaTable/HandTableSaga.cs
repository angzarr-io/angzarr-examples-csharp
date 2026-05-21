using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Hand.SagaTable;

/// <summary>
/// Saga: Hand -> Table (OO Pattern)
///
/// Reacts to HandComplete events from Hand domain.
/// Sends EndHand commands to Table domain.
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class HandTableSaga : Saga
{
    public override string Name => "saga-hand-table";
    public override string InputDomain => "hand";
    public override string OutputDomain => "table";

    [Handles(typeof(HandComplete))]
    public CommandBook HandleHandComplete(HandComplete evt)
    {
        var results = evt
            .Winners.Select(winner => new PotResult
            {
                WinnerRoot = winner.PlayerRoot,
                Amount = winner.Amount,
                PotType = winner.PotType,
                WinningHand = winner.WinningHand,
            })
            .ToList();

        var endHand = new EndHand();
        endHand.Results.AddRange(results);

        var cmdAny = PackCommand(endHand);

        return new CommandBook
        {
            Cover = new Cover
            {
                Domain = "table",
                Root = new UUID { Value = evt.TableRoot },
            },
            Pages =
            {
                new CommandPage
                {
                    Header = new PageHeader { AngzarrDeferred = new AngzarrDeferredSequence() },
                    Command = cmdAny,
                },
            },
        };
    }
}
