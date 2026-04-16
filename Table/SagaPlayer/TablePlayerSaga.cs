using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Table.SagaPlayer;

/// <summary>
/// Saga: Table -> Player (OO Pattern)
///
/// Reacts to HandEnded events from Table domain.
/// Sends ReleaseFunds commands to Player domain.
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class TablePlayerSaga : Saga
{
    public override string Name => "saga-table-player";
    public override string InputDomain => "table";
    public override string OutputDomain => "player";

    [Handles(typeof(HandEnded))]
    public List<CommandBook> HandleHandEnded(HandEnded evt, List<EventBook> destinations)
    {
        var commands = new List<CommandBook>();

        foreach (var playerHex in evt.StackChanges.Keys)
        {
            var playerRoot = ByteString.CopyFrom(Convert.FromHexString(playerHex));

            var releaseFunds = new ReleaseFunds { TableRoot = evt.HandRoot };

            var cmdAny = PackCommand(releaseFunds);

            commands.Add(
                new CommandBook
                {
                    Cover = new Cover
                    {
                        Domain = "player",
                        Root = new UUID { Value = playerRoot },
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
