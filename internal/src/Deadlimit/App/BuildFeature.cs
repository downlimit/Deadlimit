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
            Text = "PREPARE FOR CSDK",
            AutoSize = true,
        };

        buildButton.Click += async (_, _) => await RunPrepareAsync(form, buildButton);
        topBar.Controls.Add(buildButton);
    }

    private static async Task RunPrepareAsync(MainForm form, Button buildButton)
    {
        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                form,
                "Save the current Deadlimit project before running PREPARE FOR CSDK.",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        buildButton.Enabled = false;
        var originalTitle = form.Text;

        try
        {
            var progress = new Progress<PrepareAuthoringProgress>(update =>
            {
                form.Text = $"Deadlimit — {update.Message}";
            });

            var service = new PrepareAuthoringService(new DeadlimitPaths());
            var result = await service.PrepareAsync(manifest, progress);

            var gameState = result.GameOutputCleaned
                ? "Existing compiled output for this addon was removed."
                : "No previous compiled output for this addon existed.";

            MessageBox.Show(
                form,
                $"Authoring content prepared.\n\n" +
                $"Addon: {result.AddonName}\n" +
                $"DMX overlays: {result.DmxCount}\n" +
                $"Material remaps retained/added: {result.MaterialRemapCount}\n" +
                $"Retail source files copied: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Model source:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit did not compile it; launch CSDK12 to rebuild game from the prepared content.\n\n" +
                $"Log: {result.LogPath}",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                "Prepare failed",
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
