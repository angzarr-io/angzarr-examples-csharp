using TechTalk.SpecFlow;
using Tests.Client;

namespace Tests.Steps.Acceptance;

/// <summary>
/// SpecFlow hooks for acceptance tests. Creates the appropriate ICommandClient
/// based on environment: PLAYER_URL set -> GrpcCommandClient, otherwise -> InProcessCommandClient.
/// </summary>
[Binding]
public class AcceptanceHooks
{
    private readonly ScenarioContext _context;

    public AcceptanceHooks(ScenarioContext context)
    {
        _context = context;
    }

    [Given(@"the poker system is running in standalone mode")]
    public void GivenThePokerSystemIsRunningInStandaloneMode()
    {
        var client = CommandClientFactory.Create();
        _context["commandClient"] = client;
    }

    [AfterScenario]
    public void DisposeClient()
    {
        if (
            _context.TryGetValue("commandClient", out object? client)
            && client is IDisposable disposable
        )
        {
            disposable.Dispose();
        }
    }
}
