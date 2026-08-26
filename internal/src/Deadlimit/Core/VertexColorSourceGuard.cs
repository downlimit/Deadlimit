using System.Text;

namespace Deadlimit.Core;

public sealed record VertexColorSourceState(
    bool UsesVertexColorMaterial,
    bool HasEmbeddedVertexColor,
    string SidecarPath,
    bool SidecarExists,
    bool SidecarCurrent,
    string Message)
{
    public bool NeedsExternalSidecar => UsesVertexColorMaterial && !HasEmbeddedVertexColor;
}

public sealed record VertexColorStagedResult(
    bool Ready,
    VertexColorSidecarResult VertexColor,
    string Message);

public static class VertexColorSourceGuard
{
    public static VertexColorSourceState Inspect(string artistDmxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistDmxPath);

        var raw = File.ReadAllBytes(artistDmxPath);
        var text = Encoding.Latin1.GetString(raw);
        var usesVertexColorMaterial = text.Contains("vertexcolor", StringComparison.OrdinalIgnoreCase);
        var hasEmbeddedVertexColor = usesVertexColorMaterial
            && text.Contains("color$0", StringComparison.Ordinal)
            && text.Contains("color$0Indices", StringComparison.Ordinal);

        var sidecarPath = VertexColorSidecarService.GetSidecarPath(artistDmxPath);
        var sidecarExists = File.Exists(sidecarPath);
        var sidecarCurrent = sidecarExists
            && File.GetLastWriteTimeUtc(sidecarPath) >= File.GetLastWriteTimeUtc(artistDmxPath);

        var message = !usesVertexColorMaterial
            ? "DMX does not use a Vertex Color material; no sidecar is required."
            : hasEmbeddedVertexColor
                ? "DMX already contains color$0/color$0Indices; the FBX sidecar is not required for this revision."
                : !sidecarExists
                    ? $"Vertex Color source is incomplete: {Path.GetFileName(sidecarPath)} is missing. Export the Vertex Color FBX after the latest DMX export."
                    : !sidecarCurrent
                        ? $"Vertex Color source is incomplete: {Path.GetFileName(sidecarPath)} is older than {Path.GetFileName(artistDmxPath)}. Export the Vertex Color FBX again after the latest DMX export."
                        : "Vertex Color DMX/FBX source pair is current.";

        return new VertexColorSourceState(
            usesVertexColorMaterial,
            hasEmbeddedVertexColor,
            sidecarPath,
            sidecarExists,
            sidecarCurrent,
            message);
    }

    public static IReadOnlyList<VertexColorSourceState> ValidateForPrepare(
        IEnumerable<string> artistDmxPaths,
        CancellationToken cancellationToken = default)
    {
        var states = new List<VertexColorSourceState>();
        foreach (var artistDmxPath in artistDmxPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = Inspect(artistDmxPath);
            states.Add(state);

            if (!state.NeedsExternalSidecar)
            {
                continue;
            }

            if (!state.SidecarExists || !state.SidecarCurrent)
            {
                throw new InvalidOperationException(
                    $"PREPARE stopped before changing CSDK content because {Path.GetFileName(artistDmxPath)} requires Vertex Color but its source pair is not safe.\n\n{state.Message}\n\nThe previously prepared CSDK content was left untouched.");
            }

            var validation = ValidatePair(artistDmxPath, cancellationToken);
            if (validation.Status != VertexColorSidecarStatus.Applied)
            {
                throw new InvalidOperationException(
                    $"PREPARE stopped before changing CSDK content because Vertex Color validation failed for {Path.GetFileName(artistDmxPath)}.\n\n{validation.Message}\n\nThe previously prepared CSDK content was left untouched.");
            }
        }

        return states;
    }

    public static VertexColorStagedResult PrepareStagedDmx(
        string artistDmxPath,
        string stagedDmxPath)
    {
        var state = Inspect(artistDmxPath);
        if (!state.UsesVertexColorMaterial)
        {
            var result = new VertexColorSidecarResult(
                VertexColorSidecarStatus.Skipped,
                state.SidecarPath,
                0,
                state.Message);
            return new VertexColorStagedResult(true, result, state.Message);
        }

        if (state.HasEmbeddedVertexColor)
        {
            var result = new VertexColorSidecarResult(
                VertexColorSidecarStatus.Applied,
                state.SidecarPath,
                0,
                state.Message);
            return new VertexColorStagedResult(true, result, state.Message);
        }

        if (!state.SidecarExists || !state.SidecarCurrent)
        {
            var status = state.SidecarExists
                ? VertexColorSidecarStatus.Skipped
                : VertexColorSidecarStatus.Missing;
            var result = new VertexColorSidecarResult(
                status,
                state.SidecarPath,
                0,
                state.Message);
            return new VertexColorStagedResult(false, result, state.Message);
        }

        var applied = VertexColorSidecarService.TryApply(artistDmxPath, stagedDmxPath);
        return new VertexColorStagedResult(
            applied.Status == VertexColorSidecarStatus.Applied,
            applied,
            applied.Message);
    }

    private static VertexColorSidecarResult ValidatePair(
        string artistDmxPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationPath = Path.Combine(
            Path.GetTempPath(),
            $"deadlimit-vertexcolor-validation-{Guid.NewGuid():N}.dmx");
        try
        {
            File.Copy(artistDmxPath, validationPath, overwrite: false);
            return VertexColorSidecarService.TryApply(artistDmxPath, validationPath);
        }
        finally
        {
            try
            {
                if (File.Exists(validationPath))
                {
                    File.Delete(validationPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Validation already completed; temporary cleanup is best-effort only.
            }
        }
    }
}
