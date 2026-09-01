namespace Deadlimit.Core;

internal enum ToolchainOperationTarget
{
    Csdk,
    DeadlockTools,
}

internal enum ToolchainOperationState
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

internal sealed record ToolchainOperationUpdate(
    ToolchainOperationTarget Target,
    ToolchainOperationState State,
    string Message,
    int? Percent = null);

internal static class ToolchainOperationHub
{
    private static readonly object Sync = new();
    private static CancellationTokenSource? _activeCancellation;
    private static ToolchainOperationTarget? _activeTarget;

    public static event EventHandler<ToolchainOperationUpdate>? Changed;

    public static OperationScope Begin(
        ToolchainOperationTarget target,
        CancellationToken externalToken,
        string initialMessage)
    {
        CancellationTokenSource linked;
        lock (Sync)
        {
            if (_activeCancellation is not null)
            {
                throw new InvalidOperationException(UiTextBridge.T("Another toolchain operation is already running.", "Другая операция с инструментами уже выполняется."));
            }

            _activeCancellation = new CancellationTokenSource();
            _activeTarget = target;
            linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _activeCancellation.Token);
        }

        Publish(new(target, ToolchainOperationState.Running, initialMessage, 0));
        return new OperationScope(target, linked);
    }

    public static void CancelActive()
    {
        CancellationTokenSource? cancellation;
        ToolchainOperationTarget? target;
        lock (Sync)
        {
            cancellation = _activeCancellation;
            target = _activeTarget;
        }

        if (cancellation is null || target is null || cancellation.IsCancellationRequested)
        {
            return;
        }

        Publish(new(
            target.Value,
            ToolchainOperationState.Running,
            UiTextBridge.T("Cancelling…", "Отмена…"),
            null));
        cancellation.Cancel();
    }

    public static void Report(OperationScope scope, string message, int? percent = null)
    {
        Publish(new(
            scope.Target,
            ToolchainOperationState.Running,
            message,
            percent is null ? null : Math.Clamp(percent.Value, 0, 100)));
    }

    public static void Complete(OperationScope scope, string message)
    {
        Publish(new(scope.Target, ToolchainOperationState.Completed, message, 100));
        End(scope);
    }

    public static void Fail(OperationScope scope, string message)
    {
        Publish(new(scope.Target, ToolchainOperationState.Failed, message, null));
        End(scope);
    }

    public static void Cancelled(OperationScope scope, string message)
    {
        Publish(new(scope.Target, ToolchainOperationState.Cancelled, message, null));
        End(scope);
    }

    private static void End(OperationScope scope)
    {
        lock (Sync)
        {
            if (_activeTarget == scope.Target)
            {
                _activeCancellation?.Dispose();
                _activeCancellation = null;
                _activeTarget = null;
            }
        }
    }

    private static void Publish(ToolchainOperationUpdate update)
    {
        Changed?.Invoke(null, update);
    }

    internal sealed class OperationScope : IDisposable
    {
        private readonly CancellationTokenSource _linkedCancellation;
        private bool _disposed;

        internal OperationScope(ToolchainOperationTarget target, CancellationTokenSource linkedCancellation)
        {
            Target = target;
            _linkedCancellation = linkedCancellation;
        }

        public ToolchainOperationTarget Target { get; }

        public CancellationToken Token => _linkedCancellation.Token;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _linkedCancellation.Dispose();
        }
    }

    // Core must not depend on the App namespace just to localize two tiny progress strings.
    private static class UiTextBridge
    {
        public static string T(string english, string russian) =>
            string.Equals(ProjectStore.GetToolPathSettings().UiLanguage, "ru", StringComparison.OrdinalIgnoreCase)
                ? russian
                : english;
    }
}
