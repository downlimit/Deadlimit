namespace Deadlimit.Core;

public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 2;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectFolder { get; set; } = string.Empty;
    public string Hero { get; set; } = string.Empty;
    public string? ReleaseTarget { get; set; }

    public string SourceDumpFolderName { get; set; } = "0source";
    public List<string> DmxFiles { get; set; } = [];
    public List<string> PngTextures { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? RetailMainModel { get; set; }
    public string? RetailSourceVpk { get; set; }
    public DateTimeOffset? LastSourceExtractionUtc { get; set; }
    public string? Source2ViewerVersion { get; set; }
    public int? ExtractedSourceFileCount { get; set; }

    public string? SourceVmdl { get; set; }
    public string? CompiledVmdl { get; set; }
    public List<string> AnimGraph2Refs { get; set; } = [];
    public string? NmSkeletonRef { get; set; }
}
