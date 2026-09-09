namespace Deadlimit.Core;

public sealed record HeroExtractionOptions(
    bool ExtractTextures = false,
    bool ExtractAbilities = false,
    bool ExtractPortraitsAndUi = false,
    bool ExtractHero = true,
    bool CopyMaterialsToCsdkForEditing = false,
    bool CopyAbilityFxToCsdkForEditing = false,
    bool BackupCsdkOverwrites = true)
{
    public static HeroExtractionOptions SourceOnly { get; } = new();

    public bool HasAnyExtractionScope =>
        ExtractHero || ExtractAbilities || ExtractPortraitsAndUi;
}
