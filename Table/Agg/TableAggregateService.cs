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

            var eventMessage = agg.HandleCommand(command);

            var eventBook = new EventBook();
            var eventAny = Any.Pack(eventMessage, "type.googleapis.com/");
            eventBook.Pages.Add(
                new EventPage
                {
                    Header = new PageHeader { Sequence = request.Events?.NextSequence ?? 0 },
                    Event = eventAny,
                }
            );

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

    private static IMessage? UnpackCommand(Any commandAny)
    {
        var typeUrl = commandAny.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl.Split('/').Last() : typeUrl;

        return typeName switch
        {
            "examples.CreateTable" => commandAny.Unpack<CreateTable>(),
            "examples.JoinTable" => commandAny.Unpack<JoinTable>(),
            "examples.LeaveTable" => commandAny.Unpack<LeaveTable>(),
            "examples.SitOut" => commandAny.Unpack<SitOut>(),
            "examples.SitIn" => commandAny.Unpack<SitIn>(),
            "examples.StartHand" => commandAny.Unpack<StartHand>(),
            "examples.EndHand" => commandAny.Unpack<EndHand>(),
            "examples.AddChips" => commandAny.Unpack<AddChips>(),
            _ => null,
        };
    }
}
