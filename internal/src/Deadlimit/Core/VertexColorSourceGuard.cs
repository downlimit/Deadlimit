using System.Collections;
using System.Text;
using Datamodel;
using Datamodel.Codecs;
using DmxColor = Datamodel.Color;

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
        var hasVertexColorToken = text.Contains("vertexcolor", StringComparison.OrdinalIgnoreCase);

        var usesVertexColorMaterial = false;
        var hasEmbeddedVertexColor = false;
        var embeddedMessage = string.Empty;
        if (hasVertexColorToken)
        {
            try
            {
                (usesVertexColorMaterial, hasEmbeddedVertexColor, embeddedMessage) =
                    InspectEmbeddedVertexColor(artistDmxPath);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
            {
                // Fail closed only when the DMX itself cannot be structurally inspected.
                // A raw "vertexcolor" token is not enough to require a sidecar when
                // structural inspection succeeds and no faceSet actually assigns it.
                usesVertexColorMaterial = true;
                hasEmbeddedVertexColor = false;
                embeddedMessage = $"Vertex Color material assignment could not be validated: {ex.Message}";
            }
        }

        var sidecarPath = VertexColorSidecarService.GetSidecarPath(artistDmxPath);
        var sidecarExists = File.Exists(sidecarPath);
        var sidecarCurrent = sidecarExists
            && File.GetLastWriteTimeUtc(sidecarPath) >= File.GetLastWriteTimeUtc(artistDmxPath);

        var message = !usesVertexColorMaterial
            ? "DMX has no faceSet assigned to a Vertex Color material; no sidecar is required."
            : hasEmbeddedVertexColor
                ? embeddedMessage
                : !sidecarExists
                    ? BuildSidecarProblemMessage(
                        embeddedMessage,
                        $"Vertex Color source is incomplete: {Path.GetFileName(sidecarPath)} is missing. Export the Vertex Color FBX after the latest DMX export.")
                    : !sidecarCurrent
                        ? BuildSidecarProblemMessage(
                            embeddedMessage,
                            $"Vertex Color source is incomplete: {Path.GetFileName(sidecarPath)} is older than {Path.GetFileName(artistDmxPath)}. Export the Vertex Color FBX again after the latest DMX export.")
                        : string.IsNullOrWhiteSpace(embeddedMessage)
                            ? "Vertex Color DMX/FBX source pair is current."
                            : $"{embeddedMessage} Falling back to the current Vertex Color FBX sidecar.";

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

    private static string BuildSidecarProblemMessage(string inspectionMessage, string sidecarMessage)
    {
        return string.IsNullOrWhiteSpace(inspectionMessage)
            ? sidecarMessage
            : $"{inspectionMessage} {sidecarMessage}";
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

    private static (bool UsesVertexColorMaterial, bool HasValidEmbeddedColor, string Message)
        InspectEmbeddedVertexColor(string dmxPath)
    {
        using var document = Datamodel.Datamodel.Load(dmxPath, DeferredMode.Disabled);
        var requiredMeshes = 0;

        foreach (var mesh in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal)))
        {
            // Material assignment is the authority for whether this mesh needs Vertex Color.
            // Do not inspect vertex-state details on unrelated meshes just because the raw
            // DMX happens to contain the word "vertexcolor" elsewhere.
            var faceSets = mesh.GetArray<Element>("faceSets");
            if (faceSets is null || !faceSets.Any(UsesVertexColorMaterial))
            {
                continue;
            }

            var bindState = mesh.Get<Element>("bindState")
                ?? throw new InvalidDataException($"Vertex Color mesh '{mesh.Name}' has no bindState.");
            var currentState = mesh.Get<Element>("currentState")
                ?? throw new InvalidDataException($"Vertex Color mesh '{mesh.Name}' has no currentState.");
            if (bindState.ID != currentState.ID)
            {
                throw new InvalidDataException(
                    $"Vertex Color mesh '{mesh.Name}' uses different bind/current vertex states.");
            }

            requiredMeshes++;
            if (!HasValidColorStream(bindState, out var reason))
            {
                return (
                    true,
                    false,
                    $"Embedded Vertex Color is incomplete on mesh '{mesh.Name}': {reason}");
            }
        }

        if (requiredMeshes == 0)
        {
            return (false, false, "DMX contains no faceSet assigned to a Vertex Color material.");
        }

        return (
            true,
            true,
            $"DMX contains validated embedded color$0/color$0Indices on {requiredMeshes} Vertex Color mesh(es); the FBX sidecar is not required for this revision.");
    }

    private static bool HasValidColorStream(Element vertexData, out string reason)
    {
        if (!vertexData.ContainsKey("color$0") || !vertexData.ContainsKey("color$0Indices"))
        {
            reason = "color$0/color$0Indices is missing.";
            return false;
        }

        var colors = vertexData.GetArray<DmxColor>("color$0");
        var colorIndices = vertexData.GetArray<int>("color$0Indices");
        var vertexFormat = vertexData.GetArray<string>("vertexFormat");
        if (colors is null || colorIndices is null || vertexFormat is null)
        {
            reason = "Vertex Color arrays have an invalid DMX type.";
            return false;
        }

        var logicalVertexCount = GetLogicalVertexCount(vertexData);
        if (colors.Count == 0)
        {
            reason = "color$0 is empty.";
            return false;
        }
        if (colorIndices.Count != logicalVertexCount)
        {
            reason = $"color$0Indices has {colorIndices.Count} entries; expected {logicalVertexCount}.";
            return false;
        }
        if (colorIndices.Any(index => index < 0 || index >= colors.Count))
        {
            reason = "color$0Indices references a color outside color$0.";
            return false;
        }
        if (!vertexFormat.Contains("color$0", StringComparer.Ordinal))
        {
            reason = "vertexFormat does not contain color$0.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static int GetLogicalVertexCount(Element vertexData)
    {
        if (vertexData.ContainsKey("position$0Indices"))
        {
            var positionIndices = vertexData.GetArray<int>("position$0Indices");
            if (positionIndices is null)
            {
                throw new InvalidDataException("position$0Indices has an invalid DMX type.");
            }
            return positionIndices.Count;
        }

        var rawPositions = vertexData["position$0"];
        if (rawPositions is not IEnumerable enumerable || rawPositions is string)
        {
            throw new InvalidDataException("position$0 is missing or is not an array.");
        }

        return enumerable.Cast<object>().Count();
    }

    private static bool UsesVertexColorMaterial(Element faceSet)
    {
        if (!faceSet.ContainsKey("material"))
        {
            return false;
        }

        var material = faceSet.Get<Element>("material");
        if (material is null)
        {
            return false;
        }

        if (material.Name?.Contains("vertexcolor", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return material.ContainsKey("mtlName")
            && material.Get<string>("mtlName")?.Contains(
                "vertexcolor",
                StringComparison.OrdinalIgnoreCase) == true;
    }
}
