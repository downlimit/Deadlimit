using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class BuildAndTestService
{
    private readonly DeadlimitPaths _paths;

    public BuildAndTestService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<BuildAndTestResult> BuildAsync(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Mode == ProjectMode.ImportedVpk)
        {
            return Task.FromResult(
                new ImportedVpkBuildAndTestService(_paths)
                    .Build(manifest, progress, cancellationToken));
        }

        return new Deadlimit.Core.BuildAndTestService(_paths)
            .BuildAsync(manifest, progress, cancellationToken);
    }
}
