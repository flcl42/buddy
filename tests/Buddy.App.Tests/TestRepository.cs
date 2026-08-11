namespace Buddy.App.Tests;

internal static class TestRepository
{
    public static string Path(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        string? root = FindRoot();
        if (root is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate the Buddy repository root from the test process.");
        }

        return System.IO.Path.Combine([root, .. segments]);
    }

    private static string? FindRoot()
    {
        foreach (string? seed in new[]
        {
            Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"),
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            DirectoryInfo? directory = new(System.IO.Path.GetFullPath(seed));
            while (directory is not null)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "Buddy.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
