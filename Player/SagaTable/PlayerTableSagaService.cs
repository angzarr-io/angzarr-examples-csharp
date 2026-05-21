using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Player.SagaTable;

/// <summary>
/// gRPC service for Player->Table saga (OO pattern).
///
/// Emits facts (events) to table domain for sit-out/sit-in tracking.
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class PlayerTableSagaService : SagaService.SagaServiceBase
{
    private readonly PlayerTableSaga _saga = new();

    public override Task<SagaResponse> Handle(SagaHandleRequest request, ServerCallContext context)
    {
        var response = new SagaResponse();
        _saga.SetSourceRoot(request.Source);
        var root = request.Source.Cover?.Root?.Value.ToByteArray();
        var correlationId = request.Source.Cover?.CorrelationId ?? "";

        foreach (var page in request.Source.Pages)
        {
            if (page.Event == null)
                continue;
            response.Commands.AddRange(_saga.Dispatch(page.Event, root, correlationId));
        }

        // Facts emitted via EmitFact() are collected here
        response.Events.AddRange(_saga.CollectEvents());

        return Task.FromResult(response);
    }
}
