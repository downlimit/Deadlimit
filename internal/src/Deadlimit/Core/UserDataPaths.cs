namespace Deadlimit.Core;

public static class UserDataPaths
{
    private const string ProductFolderName = "Deadlimit";
    internal const string TestRootEnvironmentVariable = "DEADLIMIT_TEST_USER_DATA_ROOT";

    public static string Root => ResolveRoot();

    internal static string ResolveRoot() =>
        Environment.GetEnvironmentVariable(TestRootEnvironmentVariable) is { Length: > 0 } testRoot
            ? Path.GetFullPath(testRoot)
            : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);

    internal static IDisposable IsolateForTests() => new TestIsolation();

    private sealed class TestIsolation : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(TestRootEnvironmentVariable);
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"deadlimit-test-user-data-{Guid.NewGuid():N}");

        public TestIsolation()
        {
            Directory.CreateDirectory(_root);
            Environment.SetEnvironmentVariable(TestRootEnvironmentVariable, _root);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(TestRootEnvironmentVariable, _previous);
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Combine(params string[] paths)
    {
        var segments = new string[paths.Length + 1];
        segments[0] = Root;
        Array.Copy(paths, 0, segments, 1, paths.Length);
        return Path.Combine(segments);
    }
}
