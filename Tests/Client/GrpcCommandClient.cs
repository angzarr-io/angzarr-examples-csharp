using Angzarr;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace Tests.Client;

/// <summary>
/// gRPC command client that connects to deployed aggregate coordinator services.
/// Used for acceptance tests against a running system.
///
/// Each domain maps to a separate coordinator URL. The player URL is required;
/// table and hand URLs default to ports 1311 and 1312 on the same host.
/// </summary>
public class GrpcCommandClient : ICommandClient, IDisposable
{
    private readonly Dictionary<string, GrpcChannel> _channels = new();
    private readonly Dictionary<string, string> _domainUrls;

    public GrpcCommandClient(string playerUrl, string? tableUrl = null, string? handUrl = null)
    {
        // Derive table/hand URLs from player URL if not explicitly set.
        var playerUri = new Uri(playerUrl);
        var baseHost = playerUri.Host;

        _domainUrls = new Dictionary<string, string>
        {
            ["player"] = playerUrl,
            ["table"] = tableUrl ?? $"{playerUri.Scheme}://{baseHost}:1311",
            ["hand"] = handUrl ?? $"{playerUri.Scheme}://{baseHost}:1312",
        };
    }

    public async Task<CommandResponse> SendCommandAsync(
        string domain,
        UUID rootId,
        IMessage command,
        uint sequence = 0
    )
    {
        var client = GetClient(domain);

        var commandAny = new Any
        {
            TypeUrl = $"type.googleapis.com/{TypeNameForCommand(command)}",
            Value = command.ToByteString(),
        };

        var request = new CommandRequest
        {
            Command = new CommandBook
            {
                Cover = new Cover
                {
                    Domain = domain,
                    Root = rootId,
                    CorrelationId = Guid.NewGuid().ToString(),
                },
                Pages =
                {
                    new CommandPage
                    {
                        Header = new PageHeader { Sequence = sequence },
                        Command = commandAny,
                    },
                },
            },
            SyncMode = SyncMode.Simple,
        };

        return await client.HandleCommandAsync(request);
    }

    private CommandHandlerCoordinatorService.CommandHandlerCoordinatorServiceClient GetClient(
        string domain
    )
    {
        if (!_domainUrls.TryGetValue(domain, out var url))
            throw new ArgumentException($"No URL configured for domain: {domain}");

        if (!_channels.TryGetValue(domain, out var channel))
        {
            channel = GrpcChannel.ForAddress(url);
            _channels[domain] = channel;
        }

        return new CommandHandlerCoordinatorService.CommandHandlerCoordinatorServiceClient(channel);
    }

    /// <summary>
    /// Derive the proto type URL suffix from the command message's descriptor.
    /// E.g. RegisterPlayer -> "examples.RegisterPlayer"
    /// </summary>
    private static string TypeNameForCommand(IMessage command)
    {
        return command.Descriptor.FullName;
    }

    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }
        _channels.Clear();
    }
}
