using Buddy.App.State;
using Buddy.App.ViewModels;

namespace Buddy.App.Services;

public interface IDesktopTrayService : IDisposable
{
    Task InitializeAsync(
        MainViewModel viewModel,
        CancellationToken cancellationToken = default);

    void Update(BuddyRuntimeMode mode, string toolTip);
}

public sealed class NullDesktopTrayService : IDesktopTrayService
{
    public Task InitializeAsync(
        MainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Update(BuddyRuntimeMode mode, string toolTip)
    {
    }

    public void Dispose()
    {
    }
}
