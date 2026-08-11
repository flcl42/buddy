namespace Buddy.App.ViewModels;

public sealed class WelcomeSetupDownloadGate
{
    public bool HasUserRequestedSetup { get; private set; }

    public void AuthorizeUserRequestedSetup()
    {
        HasUserRequestedSetup = true;
    }

    public void DemandUserRequestedSetup()
    {
        if (!HasUserRequestedSetup)
        {
            throw new InvalidOperationException(
                "Welcome-screen downloads require the user's Download and set up action.");
        }
    }
}
