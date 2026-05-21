using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Hand.SagaPlayer;

/// <summary>
/// gRPC service for Hand->Player saga (OO pattern).
/// Sagas are stateless translators - framework handles sequence stamping.
/// </summary>
public class HandPlayerSagaService : SagaService.SagaServiceBase
{
    private readonly HandPlayerSaga _saga = new();

    public override Task<SagaResponse> Handle(SagaHandleRequest request, ServerCallContext context)
    {
        var response = new SagaResponse();
        var root = request.Source.Cover?.Root?.Value.ToByteArray();
        var correlationId = request.Source.Cover?.CorrelationId ?? "";

        foreach (var page in request.Source.Pages)
        {
            if (page.Event == null)
                continue;
            response.Commands.AddRange(_saga.Dispatch(page.Event, root, correlationId));
        }

        return Task.FromResult(response);
    }
}
