using System.Diagnostics;

namespace Deadlimit.Core;

public sealed record ToolProbe(string Name, string Path, bool Exists, string? Version = null);

public sealed class DeadlimitPaths
{
    public static string DefaultDeadlimitRoot { get; } = ResolveDeadlimitRoot();
    public static string DefaultWorkspaceRoot { get; } = ResolveWorkspaceRoot(DefaultDeadlimitRoot);
    public static string DefaultCsdkRoot { get; } = Path.Combine(DefaultWorkspaceRoot, "Reduced_CSDK_12");
    public static string DefaultDeadlockToolsRoot { get; } = Path.Combine(DefaultWorkspaceRoot, "DeadlockTools");
    public const string DefaultRetailDeadlockRoot = "";

    public DeadlimitPaths()
        : this(ProjectStore.GetToolPathSettings())
    {
    }

    public DeadlimitPaths(ToolPathSettings configuredPaths)
    {
        WorkspaceRoot = DefaultWorkspaceRoot;
        DeadlimitRoot = DefaultDeadlimitRoot;
        CsdkRoot = UseConfiguredOrDefault(configuredPaths.CsdkRoot, DefaultCsdkRoot);
        DeadlockToolsRoot = UseConfiguredOrDefault(configuredPaths.DeadlockToolsRoot, DefaultDeadlockToolsRoot);
        RetailDeadlockRoot = UseConfiguredOrDefault(configuredPaths.RetailDeadlockRoot, DefaultRetailDeadlockRoot);
    }

    public string WorkspaceRoot { get; }
    public string DeadlimitRoot { get; }
    public string CsdkRoot { get; }
    public string DeadlockToolsRoot { get; }
    public string RetailDeadlockRoot { get; }

    public string CsdkContentRoot => Path.Combine(CsdkRoot, "content");
    public string CsdkGameRoot => Path.Combine(CsdkRoot, "game");
    public string CsdkLauncherPath => Path.Combine(CsdkRoot, "csdkcfg.exe");
    public string ResourceCompilerPath => Path.Combine(CsdkRoot, "game", "bin_cs2", "win64", "resourcecompiler.exe");
    public string VpkPackerPath => Path.Combine(CsdkRoot, "game", "bin", "win64", "CSDKCfgVPK.exe");
    public string DeadlockToolsExePath => ResolveDeadlockToolsExecutable(DeadlockToolsRoot);

    public IReadOnlyList<ToolProbe> ProbeTools() =>
    [
        Probe("CSDK launcher", CsdkLauncherPath),
        Probe("ResourceCompiler", ResourceCompilerPath),
        Probe("CSDKCfgVPK", VpkPackerPath),
        Probe("DeadlockTools", DeadlockToolsExePath),
    ];

    private static string UseConfiguredOrDefault(string configured, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        return Path.GetFullPath(candidate.Trim());
    }

    private static string ResolveDeadlockToolsExecutable(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        var managedRelease = Path.Combine(root, "DeadlockTools.exe");
        if (File.Exists(managedRelease))
        {
            return managedRelease;
        }

        return Path.Combine(root, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");
    }

    private static string ResolveDeadlimitRoot()
    {
        foreach (var seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var root = FindRepositoryRoot(seed);
            if (root is not null)
            {
                return root;
            }
        }

        return Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveWorkspaceRoot(string deadlimitRoot)
    {
        var parent = Directory.GetParent(deadlimitRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            return parent;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile) ? deadlimitRoot : userProfile;
    }

    private static string? FindRepositoryRoot(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return null;
        }

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(seed));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return null;
        }

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DeadlimitManager.cmd"))
                || File.Exists(Path.Combine(current.FullName, "internal", "src", "Deadlimit", "Deadlimit.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static ToolProbe Probe(string name, string path)
    {
        if (!File.Exists(path))
        {
            return new ToolProbe(name, path, false);
        }

        string? version = null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            version = info.FileVersion ?? info.ProductVersion;
        }
        catch
        {
            // Version metadata is diagnostic-only. Presence is the required invariant.
        }

        return new ToolProbe(name, path, true, version);
    }
}
