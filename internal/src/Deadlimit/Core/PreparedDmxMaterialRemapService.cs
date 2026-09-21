using Datamodel;
using Datamodel.Codecs;

namespace Deadlimit.Core;

internal static class PreparedDmxMaterialRemapService
{
    public static int Apply(
        string preparedDmxPath,
        IEnumerable<VmdlMaterialRemap> remaps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedDmxPath);
        ArgumentNullException.ThrowIfNull(remaps);

        var remapByKey = BuildRemapMap(remaps);
        if (remapByKey.Count == 0)
        {
            return 0;
        }

        var temporaryPath = preparedDmxPath + $".deadlimit-material-remap-{Guid.NewGuid():N}.tmp";
        try
        {
            using var document = Datamodel.Datamodel.Load(
                preparedDmxPath,
                DeferredMode.Disabled);

            var changedElements = 0;
            foreach (var material in document.AllElements.Where(element =>
                         string.Equals(element.ClassName, "DmeMaterial", StringComparison.Ordinal)))
            {
                var changed = false;

                if (TryResolve(material.Name, remapByKey, out var remappedName)
                    && !string.Equals(material.Name, remappedName, StringComparison.Ordinal))
                {
                    material.Name = remappedName;
                    changed = true;
                }

                if (material.ContainsKey("mtlName"))
                {
                    var currentMtlName = material.Get<string>("mtlName");
                    if (TryResolve(currentMtlName, remapByKey, out var remappedMtlName)
                        && !string.Equals(currentMtlName, remappedMtlName, StringComparison.Ordinal))
                    {
                        material["mtlName"] = remappedMtlName;
                        changed = true;
                    }
                }

                if (changed)
                {
                    changedElements++;
                }
            }

            if (changedElements == 0)
            {
                return 0;
            }

            document.Save(
                temporaryPath,
                document.Encoding,
                document.EncodingVersion);
            File.Move(temporaryPath, preparedDmxPath, overwrite: true);
            return changedElements;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A cleanup failure must not invalidate an otherwise completed PREPARE.
            }
        }
    }

    private static Dictionary<string, string> BuildRemapMap(
        IEnumerable<VmdlMaterialRemap> remaps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var remap in remaps)
        {
            var from = NormalizeMaterialReference(remap.From);
            var to = NormalizeMaterialReference(remap.To);
            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            result.TryAdd(MakeMaterialKey(from), to);
        }

        return result;
    }

    private static bool TryResolve(
        string? materialReference,
        IReadOnlyDictionary<string, string> remapByKey,
        out string remapped)
    {
        remapped = string.Empty;
        var normalized = NormalizeMaterialReference(materialReference);
        if (normalized.Length == 0)
        {
            return false;
        }

        return remapByKey.TryGetValue(MakeMaterialKey(normalized), out remapped!);
    }

    private static string MakeMaterialKey(string value)
    {
        var normalized = NormalizeMaterialReference(value);
        return normalized.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^".vmat".Length]
            : normalized;
    }

    private static string NormalizeMaterialReference(string? value) =>
        value?
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/')
        ?? string.Empty;
}
