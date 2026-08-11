using Buddy.App.ViewModels;

namespace Buddy.App.Tests;

public sealed class WelcomeSetupDownloadGateTests
{
    [Fact]
    public void GateRejectsModelWorkBeforeTheUserRequestsSetup()
    {
        WelcomeSetupDownloadGate gate = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            gate.DemandUserRequestedSetup);

        Assert.False(gate.HasUserRequestedSetup);
        Assert.Contains("Download and set up", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GateStaysOpenAfterTheUserRequestsSetup()
    {
        WelcomeSetupDownloadGate gate = new();

        gate.AuthorizeUserRequestedSetup();

        Assert.True(gate.HasUserRequestedSetup);
        gate.DemandUserRequestedSetup();
        gate.DemandUserRequestedSetup();
    }
}
