using Angzarr;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Hand.Agg;
using Player.Agg;
using Table.Agg;

namespace Tests.Client;

/// <summary>
/// In-process command client that dispatches commands directly to aggregate
/// handler instances. Used for unit and integration tests (no network).
///
/// Maintains per-root event history so aggregates can be rehydrated across
/// multiple commands to the same root.
/// </summary>
public class InProcessCommandClient : ICommandClient
{
    /// <summary>
    /// Stores accumulated events per (domain, rootId) so that repeated commands
    /// to the same aggregate root replay prior events before handling.
    /// </summary>
    private readonly Dictionary<string, EventBook> _eventStore = new();

    public Task<CommandResponse> SendCommandAsync(
        string domain,
        UUID rootId,
        IMessage command,
        uint sequence = 0
    )
    {
        var key = $"{domain}:{rootId}";
        if (!_eventStore.TryGetValue(key, out var priorEvents))
        {
            priorEvents = new EventBook
            {
                Cover = new Cover
                {
                    Domain = domain,
                    Root = rootId,
                },
            };
            _eventStore[key] = priorEvents;
        }

        // Create the appropriate aggregate, rehydrate, and dispatch.
        var resultEvent = DispatchToAggregate(domain, priorEvents, command);

        // Record the resulting event in the store for future rehydration.
        var eventAny = Any.Pack(resultEvent, "type.googleapis.com/");
        var eventPage = new EventPage
        {
            Header = new PageHeader { Sequence = sequence },
            Event = eventAny,
        };
        priorEvents.Pages.Add(eventPage);

        // Build a CommandResponse matching the gRPC shape.
        var responseEventBook = new EventBook
        {
            Cover = new Cover
            {
                Domain = domain,
                Root = rootId,
            },
        };
        responseEventBook.Pages.Add(eventPage);

        var response = new CommandResponse { Events = responseEventBook };
        return Task.FromResult(response);
    }

    /// <summary>
    /// Clone an EventBook so that Rehydrate (which clears pages internally)
    /// does not destroy our stored event history.
    /// </summary>
    private static EventBook CloneEventBook(EventBook source)
    {
        var clone = new EventBook();
        if (source.Cover != null)
            clone.Cover = source.Cover.Clone();
        clone.Pages.AddRange(source.Pages);
        return clone;
    }

    private static IMessage DispatchToAggregate(
        string domain,
        EventBook priorEvents,
        IMessage command
    )
    {
        // Clone to protect stored events from Rehydrate's internal Clear().
        var cloned = CloneEventBook(priorEvents);

        switch (domain)
        {
            case "player":
            {
                var agg = new PlayerAggregate();
                agg.Rehydrate(cloned);
                return agg.HandleCommand(command);
            }
            case "table":
            {
                var agg = new TableAggregate();
                agg.Rehydrate(cloned);
                return agg.HandleCommand(command);
            }
            case "hand":
            {
                var agg = new HandAggregate();
                agg.Rehydrate(cloned);
                return agg.HandleCommand(command);
            }
            default:
                throw new ArgumentException($"Unknown domain: {domain}");
        }
    }
}
