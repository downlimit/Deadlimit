using System.Diagnostics;

namespace Deadlimit.Core;

public sealed record ToolProbe(string Name, string Path, bool Exists, string? Version = null);

public sealed class DeadlimitPaths
{
    public const string DefaultWorkspaceRoot = @"C:\WorkProjects\Deadlock";
    public const string DefaultDeadlimitRoot = @"C:\WorkProjects\Deadlock\Deadlimit";
    public const string DefaultCsdkRoot = @"C:\WorkProjects\Deadlock\Reduced_CSDK_12";
    public const string DefaultDeadlockToolsRoot = @"C:\WorkProjects\Deadlock\DeadlockTools";
    public const string DefaultRetailDeadlockRoot = @"D:\Program Files (x86)\Steam\steamapps\common\Project8Staging";

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
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        return Path.GetFullPath(configured.Trim());
    }

    private static string ResolveDeadlockToolsExecutable(string root)
    {
        var managedRelease = Path.Combine(root, "DeadlockTools.exe");
        if (File.Exists(managedRelease))
        {
            return managedRelease;
        }

        return Path.Combine(root, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");
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
