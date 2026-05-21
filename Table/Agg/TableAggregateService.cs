using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Table.Agg;

/// <summary>
/// gRPC service for the Table aggregate (OO pattern).
///
/// Uses the TableAggregate class with [Handles] attribute-based dispatch.
/// </summary>
public class TableAggregateService : CommandHandlerService.CommandHandlerServiceBase
{
    public override Task<BusinessResponse> Handle(
        ContextualCommand request,
        ServerCallContext context
    )
    {
        try
        {
            var commandAny = request.Command?.Pages.FirstOrDefault()?.Command;
            if (commandAny == null)
            {
                return Task.FromResult(
                    new BusinessResponse
                    {
                        Revocation = new RevocationResponse
                        {
                            Reason = "No command in request",
                            Abort = true,
                        },
                    }
                );
            }

            var command = UnpackCommand(commandAny);
            if (command == null)
            {
                return Task.FromResult(
                    new BusinessResponse
                    {
                        Revocation = new RevocationResponse
                        {
                            Reason = $"Unknown command type: {commandAny.TypeUrl}",
                            Abort = true,
                        },
                    }
                );
            }

            // Rehydrate aggregate from events
            var agg = new TableAggregate();
            if (request.Events != null)
                agg.Rehydrate(request.Events);

            // Dispatch records every emitted event into the aggregate's
            // EventBook (single-event handlers in Table today, but matches
            // Hand's multi-event AwardPot pattern for forward compat).
            agg.Dispatch(commandAny);

            var aggBook = agg.EventBook();
            var eventBook = new EventBook();
            var seq = request.Events?.NextSequence ?? 0;
            foreach (var page in aggBook.Pages)
            {
                eventBook.Pages.Add(
                    new EventPage
                    {
                        Header = new PageHeader { Sequence = seq++ },
                        Event = page.Event,
                    }
                );
            }

            return Task.FromResult(new BusinessResponse { Events = eventBook });
        }
        catch (CommandRejectedError ex)
        {
            return Task.FromResult(
                new BusinessResponse
                {
                    Revocation = new RevocationResponse { Reason = ex.Message, Abort = true },
                }
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                new BusinessResponse
                {
                    Revocation = new RevocationResponse { Reason = ex.Message, Abort = true },
                }
            );
        }
    }

    public override Task<ReplayResponse> Replay(ReplayRequest request, ServerCallContext context)
    {
        var eventBook = new EventBook();
        eventBook.Pages.AddRange(request.Events);
        var state = TableState.FromEventBook(eventBook);

        var response = new ReplayResponse();
        return Task.FromResult(response);
    }

    // Canonical proto FQN per `package angzarr_client.proto.examples;` in
    // proto/angzarr_client/proto/examples/table.proto. The historical
    // "examples.X" switch keys were stale — every wire-format command
    // type-URL carries the full descriptor name. New post-bump command
    // types (SeatPlayer, AddRebuyChips, hand-for-hand) are also enumerated
    // so the wider command surface is reachable through this gate.
    private static IMessage? UnpackCommand(Any commandAny)
    {
        var typeUrl = commandAny.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl.Split('/').Last() : typeUrl;

        return typeName switch
        {
            "angzarr_client.proto.examples.v1.CreateTable" => commandAny.Unpack<CreateTable>(),
            "angzarr_client.proto.examples.v1.JoinTable" => commandAny.Unpack<JoinTable>(),
            "angzarr_client.proto.examples.v1.LeaveTable" => commandAny.Unpack<LeaveTable>(),
            "angzarr_client.proto.examples.v1.SitOut" => commandAny.Unpack<SitOut>(),
            "angzarr_client.proto.examples.v1.SitIn" => commandAny.Unpack<SitIn>(),
            "angzarr_client.proto.examples.v1.StartHand" => commandAny.Unpack<StartHand>(),
            "angzarr_client.proto.examples.v1.EndHand" => commandAny.Unpack<EndHand>(),
            "angzarr_client.proto.examples.v1.AddChips" => commandAny.Unpack<AddChips>(),
            "angzarr_client.proto.examples.v1.SeatPlayer" => commandAny.Unpack<SeatPlayer>(),
            "angzarr_client.proto.examples.v1.AddRebuyChips" => commandAny.Unpack<AddRebuyChips>(),
            "angzarr_client.proto.examples.v1.EnterTableHandForHand" => commandAny.Unpack<EnterTableHandForHand>(),
            "angzarr_client.proto.examples.v1.MarkTableHandForHandHandComplete"
                => commandAny.Unpack<MarkTableHandForHandHandComplete>(),
            "angzarr_client.proto.examples.v1.EndTableHandForHand" => commandAny.Unpack<EndTableHandForHand>(),
            _ => null,
        };
    }
}
