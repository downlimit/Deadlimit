namespace Deadlimit.Core;

[Flags]
public enum PrepareResetSections
{
    None = 0,
    Materials = 1 << 0,
    Physics = 1 << 1,
    Effects = 1 << 2,
}

public sealed record PrepareAuthoringOptions(
    PrepareResetSections ResetSections = PrepareResetSections.None,
    bool CreateBackup = true)
{
    public static PrepareAuthoringOptions PreserveArtistWork { get; } = new();

    public bool Resets(PrepareResetSections section) => (ResetSections & section) != 0;
}
