using System.Collections.Concurrent;

namespace Buddy.Proxy;

public sealed class ClientRequestLock
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

    public async ValueTask<IAsyncDisposable> EnterAsync(
        long clientId,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore = _locks.GetOrAdd(
            clientId,
            static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
