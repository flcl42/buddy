namespace Buddy.Persistence.Tests;

internal sealed class TemporaryBuddyStore : IAsyncDisposable
{
    private TemporaryBuddyStore(
        string rootPath,
        BuddyDataPaths paths,
        SqliteConnectionFactory connections,
        BuddyDatabase database)
    {
        RootPath = rootPath;
        Paths = paths;
        Connections = connections;
        Database = database;
    }

    public string RootPath { get; }

    public BuddyDataPaths Paths { get; }

    public SqliteConnectionFactory Connections { get; }

    public BuddyDatabase Database { get; }

    public static async Task<TemporaryBuddyStore> CreateAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "buddy-tests", Guid.NewGuid().ToString("N"));
        BuddyDataPaths paths = new(root);
        SqliteConnectionFactory connections = new(paths);
        BuddyDatabase database = new(paths, connections);
        await database.InitializeAsync().ConfigureAwait(false);
        return new TemporaryBuddyStore(root, paths, connections, database);
    }

    public ValueTask DisposeAsync()
    {
        Database.Dispose();
        SqliteConnectionFactory.ClearPool();

        string expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "buddy-tests"))
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string actualRoot = Path.GetFullPath(RootPath);
        if (!actualRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a test path outside the Buddy test root.");
        }

        if (Directory.Exists(actualRoot))
        {
            Directory.Delete(actualRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
