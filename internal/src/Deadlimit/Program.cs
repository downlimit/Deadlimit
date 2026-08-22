using Deadlimit.Core;

return args.Length == 0
    ? ShowHelp()
    : args[0].ToLowerInvariant() switch
    {
        "doctor" => RunDoctor(),
        "--help" or "-h" or "help" => ShowHelp(),
        _ => UnknownCommand(args[0]),
    };

static int ShowHelp()
{
    Console.WriteLine("Deadlimit");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  doctor   Validate the local Deadlock modding toolchain.");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run with --help to see available commands.");
    return 2;
}

static int RunDoctor()
{
    var paths = new DeadlimitPaths();
    var failures = 0;

    Console.WriteLine("Deadlimit doctor");
    Console.WriteLine("================");
    Console.WriteLine();

    failures += PrintDirectory("Deadlock workspace", paths.WorkspaceRoot);
    failures += PrintDirectory("Deadlimit repository", paths.DeadlimitRoot);
    failures += PrintDirectory("Retail Deadlock", paths.RetailDeadlockRoot);
    failures += PrintDirectory("CSDK root", paths.CsdkRoot);
    failures += PrintDirectory("CSDK source/content", paths.CsdkContentRoot);
    failures += PrintDirectory("CSDK compiled/game", paths.CsdkGameRoot);
    failures += PrintDirectory("DeadlockTools root", paths.DeadlockToolsRoot);

    Console.WriteLine();
    Console.WriteLine("Tools");
    Console.WriteLine("-----");

    foreach (var tool in paths.ProbeTools())
    {
        var status = tool.Exists ? "OK" : "MISSING";
        var version = string.IsNullOrWhiteSpace(tool.Version) ? string.Empty : $"  version={tool.Version}";
        Console.WriteLine($"[{status}] {tool.Name}");
        Console.WriteLine($"       {tool.Path}{version}");

        // The VPK packer is not required until Stage 3.
        if (!tool.Exists && tool.Name != "CSDKCfgVPK")
        {
            failures++;
        }
    }

    Console.WriteLine();
    if (failures == 0)
    {
        Console.WriteLine("RESULT: READY");
        return 0;
    }

    Console.Error.WriteLine($"RESULT: NOT READY ({failures} required check(s) failed)");
    return 1;
}

static int PrintDirectory(string label, string path)
{
    var exists = Directory.Exists(path);
    Console.WriteLine($"[{(exists ? "OK" : "MISSING")}] {label}");
    Console.WriteLine($"       {path}");
    return exists ? 0 : 1;
}
