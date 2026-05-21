using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Hand.Agg;

/// <summary>
/// gRPC service for the Hand aggregate (OO pattern).
///
/// Uses the HandAggregate class with [Handles] attribute-based dispatch.
/// </summary>
public class HandAggregateService : CommandHandlerService.CommandHandlerServiceBase
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
            var agg = new HandAggregate();
            if (request.Events != null)
                agg.Rehydrate(request.Events);

            // Dispatch records every emitted event into the aggregate's
            // EventBook (multi-event handlers such as AwardPot emit both
            // PotAwarded and HandComplete — see Hand.cs HandleAwardPot).
            // Using Dispatch + the aggregate's EventBook (rather than the
            // legacy single-event HandleCommand path) ensures downstream
            // consumers see *every* event the handler produced.
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
        var state = HandState.FromEventBook(eventBook);

        var response = new ReplayResponse();
        return Task.FromResult(response);
    }

    // Canonical proto FQN per `package angzarr_client.proto.examples;` in
    // proto/angzarr_client/proto/examples/hand.proto. The historical
    // "examples.X" switch keys were stale — every wire-format command
    // type-URL carries the full descriptor name, so they never matched
    // and every command hit the "Unknown command type" branch. The
    // service is reached via the gRPC entry point in production but the
    // framework `Dispatch` path now handles dispatch via the generated
    // descriptor; this switch remains for the small set of unit tests
    // and legacy command-handling code that calls it directly.
    private static IMessage? UnpackCommand(Any commandAny)
    {
        var typeUrl = commandAny.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl.Split('/').Last() : typeUrl;

        return typeName switch
        {
            "angzarr_client.proto.examples.v1.DealCards" => commandAny.Unpack<DealCards>(),
            "angzarr_client.proto.examples.v1.PostBlind" => commandAny.Unpack<PostBlind>(),
            "angzarr_client.proto.examples.v1.PlayerAction" => commandAny.Unpack<PlayerAction>(),
            "angzarr_client.proto.examples.v1.DealCommunityCards" => commandAny.Unpack<DealCommunityCards>(),
            "angzarr_client.proto.examples.v1.RequestDraw" => commandAny.Unpack<RequestDraw>(),
            "angzarr_client.proto.examples.v1.RevealCards" => commandAny.Unpack<RevealCards>(),
            "angzarr_client.proto.examples.v1.AwardPot" => commandAny.Unpack<AwardPot>(),
            "angzarr_client.proto.examples.v1.StartActionClock" => commandAny.Unpack<StartActionClock>(),
            "angzarr_client.proto.examples.v1.DeclareAction" => commandAny.Unpack<DeclareAction>(),
            "angzarr_client.proto.examples.v1.PullBackPriorChip" => commandAny.Unpack<PullBackPriorChip>(),
            "angzarr_client.proto.examples.v1.CorrectIllegalBet" => commandAny.Unpack<CorrectIllegalBet>(),
            _ => null,
        };
    }
}
