namespace Deadlimit.Core;

public static class ApplicationMutationCoordinator
{
    private static readonly object Sync = new();
    private static string? _activeOperation;
    private static long _generation;

    public static bool IsBusy
    {
        get
        {
            lock (Sync)
            {
                return _activeOperation is not null;
            }
        }
    }

    public static string? ActiveOperation
    {
        get
        {
            lock (Sync)
            {
                return _activeOperation;
            }
        }
    }

    public static IDisposable Begin(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("Mutation operation name is empty.", nameof(operation));
        }

        lock (Sync)
        {
            if (_activeOperation is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot start {operation} while {_activeOperation} is still running.");
            }

            _activeOperation = operation.Trim();
            var generation = ++_generation;
            return new Scope(generation);
        }
    }

    public static void ThrowIfBusy(string operation)
    {
        lock (Sync)
        {
            if (_activeOperation is null)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Cannot start {operation} while {_activeOperation} is still running.");
        }
    }

    internal static int RunSmoke()
    {
        if (IsBusy)
        {
            return 1;
        }

        using (Begin("mutation smoke"))
        {
            if (!IsBusy || !string.Equals(ActiveOperation, "mutation smoke", StringComparison.Ordinal))
            {
                return 2;
            }

            try
            {
                using var unexpected = Begin("overlap");
                return 3;
            }
            catch (InvalidOperationException)
            {
                // Expected: a second mutating operation must fail closed.
            }
        }

        return IsBusy || ActiveOperation is not null ? 4 : 0;
    }

    private static void End(long generation)
    {
        lock (Sync)
        {
            if (_generation == generation)
            {
                _activeOperation = null;
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly long _generation;
        private bool _disposed;

        public Scope(long generation)
        {
            _generation = generation;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            End(_generation);
        }
    }
}
