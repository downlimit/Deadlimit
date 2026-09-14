namespace Deadlimit.Core;

public enum HeroExtractionFormat
{
    Dmx = 0,
    Gltf = 1,
}

public sealed record HeroExtractionOptions(
    bool ExtractTextures = false,
    bool ExtractAbilities = false,
    bool ExtractPortraitsAndUi = false,
    bool ExtractHero = true,
    bool CopyMaterialsToCsdkForEditing = false,
    bool CopyAbilityFxToCsdkForEditing = false,
    bool BackupCsdkOverwrites = true,
    HeroExtractionFormat Format = HeroExtractionFormat.Dmx)
{
    public static HeroExtractionOptions SourceOnly { get; } = new();

    public bool HasAnyExtractionScope =>
        ExtractHero || ExtractAbilities || ExtractPortraitsAndUi;
}
