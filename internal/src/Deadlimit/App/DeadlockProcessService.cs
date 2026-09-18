namespace Deadlimit.App;

internal static class DeadlockProcessService
{
    private static readonly string[] ProcessNames = ["deadlock", "project8"];

    public static bool IsRunning()
    {
        foreach (var processName in ProcessNames)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // A crash-reporting process clone can deny SYNCHRONIZE access.
                        // Its presence still means the Deadlock process family has not stopped.
                        return true;
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    public static Task<bool> IsRunningAsync(CancellationToken cancellationToken = default) =>
        Task.Run(IsRunning, cancellationToken);

    public static async Task<bool> CloseAsync(CancellationToken cancellationToken = default)
    {
        var processes = await Task.Run(GetRunningProcesses, cancellationToken);
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Continue with the other processes. Closing or killing the accessible
                    // parent with its process tree can also remove this inaccessible clone.
                }
            }

            if (await WaitUntilStoppedAsync(TimeSpan.FromSeconds(4), cancellationToken))
            {
                return true;
            }

            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            return await WaitUntilStoppedAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<System.Diagnostics.Process> GetRunningProcesses()
    {
        var result = new List<System.Diagnostics.Process>();
        foreach (var processName in ProcessNames)
        {
            result.AddRange(System.Diagnostics.Process.GetProcessesByName(processName));
        }
        return result;
    }

    private static async Task<bool> WaitUntilStoppedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsRunningAsync(cancellationToken))
            {
                return true;
            }
            await Task.Delay(200, cancellationToken);
        }

        return !await IsRunningAsync(cancellationToken);
    }
}
