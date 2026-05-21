namespace Tests.Client;

/// <summary>
/// Creates a GrpcCommandClient from environment variables.
/// PLAYER_URL defaults to http://localhost:1310.
/// </summary>
public static class CommandClientFactory
{
    public static ICommandClient Create()
    {
        var playerUrl = Environment.GetEnvironmentVariable("PLAYER_URL") ?? "http://localhost:1310";
        var tableUrl = Environment.GetEnvironmentVariable("TABLE_URL");
        var handUrl = Environment.GetEnvironmentVariable("HAND_URL");

        return new GrpcCommandClient(playerUrl, tableUrl, handUrl);
    }
}
