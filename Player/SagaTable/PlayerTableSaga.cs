using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Player.SagaTable;

/// <summary>
/// Saga: Player -> Table (OO Pattern)
///
/// Propagates player sit-out/sit-in intent as facts to the table domain.
/// Sagas are stateless translators - framework handles sequence stamping.
///
/// Flow:
/// - PlayerSittingOut -> PlayerSatOut fact to table
/// - PlayerReturningToPlay -> PlayerSatIn fact to table
/// </summary>
public class PlayerTableSaga : Saga
{
    public override string Name => "saga-player-table";
    public override string InputDomain => "player";
    public override string OutputDomain => "table";

    /// <summary>
    /// Stored source root during dispatch for handler access.
    /// </summary>
    private ByteString _currentSourceRoot = ByteString.Empty;

    /// <summary>
    /// Set source root from the event book before processing.
    /// </summary>
    public void SetSourceRoot(EventBook? source)
    {
        if (source?.Cover?.Root != null)
        {
            _currentSourceRoot = source.Cover.Root.Value;
        }
        else
        {
            _currentSourceRoot = ByteString.Empty;
        }
    }

    [Handles(typeof(PlayerSittingOut))]
    public EventBook HandlePlayerSittingOut(PlayerSittingOut evt, List<EventBook> destinations)
    {
        var satOut = new PlayerSatOut { PlayerRoot = _currentSourceRoot, SatOutAt = evt.SatOutAt };

        var factAny = Any.Pack(satOut, "type.googleapis.com/");

        return new EventBook
        {
            Cover = new Cover
            {
                Domain = "table",
                Root = new UUID { Value = evt.TableRoot },
            },
            Pages =
            {
                new EventPage
                {
                    Header = new PageHeader { AngzarrDeferred = new AngzarrDeferredSequence() },
                    Event = factAny,
                },
            },
        };
    }

    [Handles(typeof(PlayerReturningToPlay))]
    public EventBook HandlePlayerReturningToPlay(
        PlayerReturningToPlay evt,
        List<EventBook> destinations
    )
    {
        var satIn = new PlayerSatIn { PlayerRoot = _currentSourceRoot, SatInAt = evt.SatInAt };

        var factAny = Any.Pack(satIn, "type.googleapis.com/");

        return new EventBook
        {
            Cover = new Cover
            {
                Domain = "table",
                Root = new UUID { Value = evt.TableRoot },
            },
            Pages =
            {
                new EventPage
                {
                    Header = new PageHeader { AngzarrDeferred = new AngzarrDeferredSequence() },
                    Event = factAny,
                },
            },
        };
    }
}
