using Angzarr;
using Angzarr.Client;
using Grpc.Core;

namespace HandFlow;

/// <summary>
/// gRPC service for Hand Flow process manager (OO pattern).
/// </summary>
public class HandFlowService : ProcessManagerService.ProcessManagerServiceBase
{
    public override Task<ProcessManagerPrepareResponse> Prepare(
        ProcessManagerPrepareRequest request,
        ServerCallContext context
    )
    {
        var covers = ProcessManager<PMState>.PrepareDestinations<HandFlowPM>(
            request.Trigger,
            request.ProcessState
        );

        var response = new ProcessManagerPrepareResponse();
        response.Destinations.AddRange(covers);
        return Task.FromResult(response);
    }

    public override Task<ProcessManagerHandleResponse> Handle(
        ProcessManagerHandleRequest request,
        ServerCallContext context
    )
    {
        // destination_sequences replaced repeated EventBook destinations in the proto.
        // This PM doesn't use destination state, so pass an empty list.
        var (commands, events) = ProcessManager<PMState>.Handle<HandFlowPM>(
            request.Trigger,
            request.ProcessState,
            new List<EventBook>()
        );

        var response = new ProcessManagerHandleResponse();
        response.Commands.AddRange(commands);
        if (events != null)
        {
            response.ProcessEvents = events;
        }

        return Task.FromResult(response);
    }
}
