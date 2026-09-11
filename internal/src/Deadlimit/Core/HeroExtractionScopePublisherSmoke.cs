namespace Deadlimit.Core;

public static class HeroExtractionScopePublisherSmoke
{
    public static void Run()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"deadlimit-scoped-source-publish-{Guid.NewGuid():N}");

        try
        {
            VerifyPartialRefresh(root);
            VerifyLegacyPartialRefresh(root);
            VerifyFullRefresh(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void VerifyPartialRefresh(string root)
    {
        var caseRoot = Path.Combine(root, "partial");
        var output = Path.Combine(caseRoot, "0source");
        var publish = Path.Combine(caseRoot, "publish");
        var freshHero = Path.Combine(caseRoot, "fresh-hero");

        Write(output, "models/hero/old.vmdl", "old hero");
        Write(output, "models/hero/shared.vmat", "old shared");
        Write(output, "particles/hero/ability.vpcf", "ability");
        Write(output, "artist/notes.txt", "artist");
        Write(freshHero, "models/hero/new.vmdl", "new hero");
        Write(freshHero, "models/hero/shared.vmat", "new shared");

        var previousState = new SourceExtractionScopeState
        {
            Scopes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [HeroExtractionScopePublisher.HeroScope] =
                [
                    "models/hero/old.vmdl",
                    "models/hero/shared.vmat",
                ],
                [HeroExtractionScopePublisher.AbilitiesScope] =
                [
                    "models/hero/shared.vmat",
                    "particles/hero/ability.vpcf",
                ],
            },
        };

        var publication = HeroExtractionScopePublisher.Prepare(
            output,
            publish,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [HeroExtractionScopePublisher.HeroScope] = freshHero,
            },
            previousState);

        AssertMissing(publish, "models/hero/old.vmdl", "stale selected-scope file survived");
        AssertContents(publish, "models/hero/new.vmdl", "new hero");
        AssertContents(publish, "models/hero/shared.vmat", "new shared");
        AssertContents(publish, "particles/hero/ability.vpcf", "ability");
        AssertContents(publish, "artist/notes.txt", "artist");

        if (!publication.State.Scopes[HeroExtractionScopePublisher.AbilitiesScope]
                .Contains("particles/hero/ability.vpcf", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unselected ability scope ownership was lost.");
        }
    }

    private static void VerifyLegacyPartialRefresh(string root)
    {
        var caseRoot = Path.Combine(root, "legacy-partial");
        var output = Path.Combine(caseRoot, "0source");
        var publish = Path.Combine(caseRoot, "publish");
        var freshUi = Path.Combine(caseRoot, "fresh-ui");

        Write(output, "models/hero/legacy.vmdl", "legacy hero");
        Write(freshUi, "panorama/images/heroes/new.png", "new ui");

        _ = HeroExtractionScopePublisher.Prepare(
            output,
            publish,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [HeroExtractionScopePublisher.PortraitsAndUiScope] = freshUi,
            },
            previousState: null);

        AssertContents(publish, "models/hero/legacy.vmdl", "legacy hero");
        AssertContents(publish, "panorama/images/heroes/new.png", "new ui");
    }

    private static void VerifyFullRefresh(string root)
    {
        var caseRoot = Path.Combine(root, "full");
        var output = Path.Combine(caseRoot, "0source");
        var publish = Path.Combine(caseRoot, "publish");
        var hero = Path.Combine(caseRoot, "hero");
        var abilities = Path.Combine(caseRoot, "abilities");
        var ui = Path.Combine(caseRoot, "ui");

        Write(output, "unknown/stale.txt", "stale");
        Write(output, "glTFpipeline/models/hero/reference.gltf", "gltf");
        Write(hero, "models/hero/new.vmdl", "hero");
        Write(abilities, "particles/hero/new.vpcf", "ability");
        Write(ui, "panorama/images/heroes/new.png", "ui");

        _ = HeroExtractionScopePublisher.Prepare(
            output,
            publish,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [HeroExtractionScopePublisher.HeroScope] = hero,
                [HeroExtractionScopePublisher.AbilitiesScope] = abilities,
                [HeroExtractionScopePublisher.PortraitsAndUiScope] = ui,
            },
            previousState: null,
            preservedRelativeDirectories: ["glTFpipeline"]);

        AssertMissing(publish, "unknown/stale.txt", "full refresh retained an unknown stale file");
        AssertContents(publish, "glTFpipeline/models/hero/reference.gltf", "gltf");
        AssertContents(publish, "models/hero/new.vmdl", "hero");
        AssertContents(publish, "particles/hero/new.vpcf", "ability");
        AssertContents(publish, "panorama/images/heroes/new.png", "ui");
    }

    private static void Write(string root, string relativePath, string contents)
    {
        var path = SafePath.ResolveUnderRoot(root, relativePath, "Scoped publisher smoke file");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void AssertContents(
        string root,
        string relativePath,
        string expected)
    {
        var path = SafePath.ResolveUnderRoot(root, relativePath, "Scoped publisher smoke assertion");
        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected scoped publish result: {relativePath}");
        }
    }

    private static void AssertMissing(string root, string relativePath, string message)
    {
        var path = SafePath.ResolveUnderRoot(root, relativePath, "Scoped publisher smoke assertion");
        if (File.Exists(path))
        {
            throw new InvalidOperationException(message);
        }
    }
}
