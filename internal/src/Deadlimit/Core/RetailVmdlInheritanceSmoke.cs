namespace Deadlimit.Core;

internal static class RetailVmdlInheritanceSmoke
{
    public static int Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadlimit-animation-{Guid.NewGuid():N}.vmdl");
        try
        {
            File.WriteAllText(path, """
                rootNode =
                {
                    children =
                    [
                        { _class = "AnimationList" children = [ { _class = "AnimFile" name = "ui_hero_select" children = [ { _class = "AnimEvent" event_class = "AE_CL_CLOTH_STIFFEN" } ] } ] },
                        { _class = "RenderMeshList" name = "artist mesh" },
                    ]
                }
                """);
            var snapshot = RetailVmdlInheritance.CaptureAuthoringAnimations(path);
            if (snapshot.Nodes.Count != 1)
            {
                return 1;
            }

            File.WriteAllText(path, """
                rootNode =
                {
                    children =
                    [
                        { _class = "AnimationList" children = [ { _class = "AnimFile" name = "ui_hero_select" } ] },
                        { _class = "RenderMeshList" name = "retail mesh" },
                    ]
                }
                """);
            RetailVmdlInheritance.RestoreAuthoringAnimations(path, snapshot);
            var restored = File.ReadAllText(path);
            if (!restored.Contains("AE_CL_CLOTH_STIFFEN", StringComparison.Ordinal)
                || !restored.Contains("retail mesh", StringComparison.Ordinal))
            {
                return 2;
            }

            return 0;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
