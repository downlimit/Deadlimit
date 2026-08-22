using System.Diagnostics;

namespace Deadlimit.Core;

public sealed record ToolProbe(string Name, string Path, bool Exists, string? Version = null);

public sealed class DeadlimitPaths
{
    public string WorkspaceRoot { get; init; } = @"C:\WorkProjects\Deadlock";
    public string DeadlimitRoot { get; init; } = @"C:\WorkProjects\Deadlock\Deadlimit";
    public string CsdkRoot { get; init; } = @"C:\WorkProjects\Deadlock\Reduced_CSDK_12";
    public string DeadlockToolsRoot { get; init; } = @"C:\WorkProjects\Deadlock\DeadlockTools";
    public string RetailDeadlockRoot { get; init; } = @"D:\Program Files (x86)\Steam\steamapps\common\Project8Staging";

    public string CsdkContentRoot => Path.Combine(CsdkRoot, "content");
    public string CsdkGameRoot => Path.Combine(CsdkRoot, "game");
    public string ResourceCompilerPath => Path.Combine(CsdkRoot, "game", "bin_cs2", "win64", "resourcecompiler.exe");
    public string VpkPackerPath => Path.Combine(CsdkRoot, "game", "bin", "win64", "CSDKCfgVPK.exe");
    public string DeadlockToolsExePath => Path.Combine(DeadlockToolsRoot, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");

    public IReadOnlyList<ToolProbe> ProbeTools() =>
    [
        Probe("ResourceCompiler", ResourceCompilerPath),
        Probe("CSDKCfgVPK", VpkPackerPath),
        Probe("DeadlockTools", DeadlockToolsExePath),
    ];

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
