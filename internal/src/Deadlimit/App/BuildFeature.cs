using Deadlimit.Core;

namespace Deadlimit.App;

internal static class BuildFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => panel.Controls.OfType<Button>()
                .Any(button => string.Equals(button.Text, "EXTRACT HERO SOURCE", StringComparison.Ordinal)));

        if (topBar is null)
        {
            return;
        }

        var buildButton = new Button
        {
            Text = "PREPARE + COMPILE",
            AutoSize = true,
        };

        buildButton.Click += async (_, _) => await RunBuildAsync(form, buildButton);
        topBar.Controls.Add(buildButton);
    }

    private static async Task RunBuildAsync(MainForm form, Button buildButton)
    {
        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                form,
                "Save the current Deadlimit project before running PREPARE + COMPILE.",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        buildButton.Enabled = false;
        var originalTitle = form.Text;

        try
        {
            var progress = new Progress<PrepareCompileProgress>(update =>
            {
                form.Text = $"Deadlimit — {update.Message}";
            });

            var service = new PrepareCompileService(new DeadlimitPaths());
            var result = await service.PrepareAndCompileAsync(manifest, progress);

            var postProcess = result.Ag2Applied
                ? $"AG2/NmSkeleton: restored ({result.NmSkeletonRef})"
                : "AG2/NmSkeleton: skipped; see build log";

            MessageBox.Show(
                form,
                $"Prepare + compile completed.\n\n" +
                $"Addon: {result.AddonName}\n" +
                $"DMX: {result.DmxCount}\n" +
                $"Material path remaps: {result.MaterialRemapCount}\n" +
                $"Compiled model: {result.CompiledVmdlPath}\n" +
                $"{postProcess}\n\n" +
                $"Log: {result.LogPath}",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                "Prepare + compile failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            form.Text = originalTitle;
            buildButton.Enabled = true;
        }
    }

    private static IEnumerable<T> FindDescendants<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
