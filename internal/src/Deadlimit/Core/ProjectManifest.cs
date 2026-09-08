using System.Text.Json.Serialization;

namespace Deadlimit.Core;

public enum ProjectMode
{
    Authoring = 0,
    ImportedVpk = 1,
}

public sealed class ImportedVpkMetadata
{
    public string SourceVpkFileName { get; set; } = string.Empty;
    public string SourceVpkPath { get; set; } = string.Empty;
    public string? SourceReleaseTarget { get; set; }
    public string OriginalVpkSha256 { get; set; } = string.Empty;
    public int SourceEntryCount { get; set; }
    public DateTimeOffset ImportedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ImporterVersion { get; set; } = string.Empty;
    public List<string> InferredHeroes { get; set; } = [];
    public List<string> PrimaryModelResources { get; set; } = [];
}

public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 4;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProjectMode Mode { get; set; } = ProjectMode.Authoring;

    public ImportedVpkMetadata? ImportedVpk { get; set; }

    public string ProjectId { get; set; } = string.Empty;
    public string AddonId { get; set; } = string.Empty;
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
