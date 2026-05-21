using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Hand.SagaTable;

/// <summary>
/// gRPC service for Hand->Table saga (OO pattern).
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class HandTableSagaService : SagaService.SagaServiceBase
{
    private readonly HandTableSaga _saga = new();

    public override Task<SagaResponse> Handle(SagaHandleRequest request, ServerCallContext context)
    {
        var response = new SagaResponse();
        var root = request.Source.Cover?.Root?.Value.ToByteArray();
        var correlationId = request.Source.Cover?.CorrelationId ?? "";

        foreach (var page in request.Source.Pages)
        {
            if (page.Event == null)
                continue;
            var commands = _saga.Dispatch(page.Event, root, correlationId);
            response.Commands.AddRange(commands);
        }

        return Task.FromResult(response);
    }
}
