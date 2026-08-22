namespace Deadlimit.Core;

public sealed class ProjectManifest
{
    public required string ProjectName { get; init; }
    public required string SourceFolder { get; init; }
    public required string Hero { get; init; }
    public string? RetailMainModel { get; init; }
    public string? SourceVmdl { get; init; }
    public string? CompiledVmdl { get; init; }
    public List<string> AnimGraph2Refs { get; init; } = [];
    public string? NmSkeletonRef { get; init; }
    public string? ReleaseTarget { get; init; }
}
