using Angzarr;
using Google.Protobuf;

namespace Tests.Client;

/// <summary>
/// Abstraction for sending commands to aggregate coordinators.
/// InProcessCommandClient wraps direct handler calls (unit/integration tests).
/// GrpcCommandClient connects to a running coordinator via gRPC (acceptance tests).
/// </summary>
public interface ICommandClient
{
    /// <summary>
    /// Send a command to the specified domain aggregate and return the response.
    /// </summary>
    /// <param name="domain">The aggregate domain (e.g. "player", "table", "hand").</param>
    /// <param name="rootId">The aggregate root ID.</param>
    /// <param name="command">The protobuf command message.</param>
    /// <param name="sequence">The expected event sequence number.</param>
    /// <returns>The command response containing resulting events.</returns>
    Task<CommandResponse> SendCommandAsync(
        string domain,
        UUID rootId,
        IMessage command,
        uint sequence = 0
    );
}
