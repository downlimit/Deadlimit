namespace Deadlimit.Core;

public sealed record HeroExtractionOptions(
    bool ExtractTextures = false,
    bool ExtractAbilities = false,
    bool ExtractPortraitsAndUi = false)
{
    public static HeroExtractionOptions SourceOnly { get; } = new();
}
