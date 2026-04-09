namespace Tests.Client;

/// <summary>
/// Creates the appropriate ICommandClient based on the PLAYER_URL environment variable.
/// If PLAYER_URL is set, uses GrpcCommandClient (acceptance tests against a running system).
/// Otherwise, uses InProcessCommandClient (unit/integration tests with direct handler calls).
/// </summary>
public static class CommandClientFactory
{
    public static ICommandClient Create()
    {
        var playerUrl = Environment.GetEnvironmentVariable("PLAYER_URL");
        var tableUrl = Environment.GetEnvironmentVariable("TABLE_URL");
        var handUrl = Environment.GetEnvironmentVariable("HAND_URL");

        if (!string.IsNullOrEmpty(playerUrl))
        {
            return new GrpcCommandClient(playerUrl, tableUrl, handUrl);
        }

        return new InProcessCommandClient();
    }
}
