using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailVmdlNode(string ClassName, string Text);

public sealed record RetailVmdlInheritanceResult(
    string? SourceVmdlPath,
    IReadOnlyList<RetailVmdlNode> Nodes)
{
    public bool Contains(string className) =>
        Nodes.Any(node => string.Equals(node.ClassName, className, StringComparison.Ordinal));
}

public static class RetailVmdlInheritance
{
    // These ModelDoc nodes are accepted by the current Reduced CSDK12 model compiler
    // and are useful to preserve from the retail character source.
    //
    // NmSkeletonList / AnimGraph2List are intentionally NOT copied into the authoring
    // VMDL. Current CSDK12 fails to instantiate those classes while loading the VMDL.
    // Runtime AG2/NmSkeleton references are restored after model compilation instead.
    private static readonly HashSet<string> PreservedClasses = new(StringComparer.Ordinal)
    {
        "BoneMarkupList",
        "Skeleton",
        "AttachmentList",
    };

    private static readonly Regex ClassRegex = new(
        "\\b_class\\s*=\\s*\"(?<class>[^\"]+)\"",
        RegexOptions.Compiled);

    public static RetailVmdlInheritanceResult Load(ProjectManifest manifest)
    {
        var sourceVmdl = FindRetailVmdl(manifest);
        if (sourceVmdl is null)
        {
            return new RetailVmdlInheritanceResult(null, Array.Empty<RetailVmdlNode>());
        }

        var text = File.ReadAllText(sourceVmdl);
        var nodes = ExtractRootChildNodes(text)
            .Where(node => PreservedClasses.Contains(node.ClassName))
            .ToArray();

        return new RetailVmdlInheritanceResult(sourceVmdl, nodes);
    }

    private static string? FindRetailVmdl(ProjectManifest manifest)
    {
        var sourceRoot = Path.Combine(manifest.ProjectFolder, manifest.SourceDumpFolderName);
        if (!Directory.Exists(sourceRoot) || string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            return null;
        }

        var retailSourceResource = ToSourceVmdlResourcePath(manifest.RetailMainModel);
        var exactPath = Path.Combine(
            sourceRoot,
            retailSourceResource.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(exactPath))
        {
            return exactPath;
        }

        var desiredName = Path.GetFileName(retailSourceResource);
        return Directory.EnumerateFiles(sourceRoot, "*.vmdl", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), desiredName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    private static IReadOnlyList<RetailVmdlNode> ExtractRootChildNodes(string text)
    {
        var rootIndex = text.IndexOf("rootNode", StringComparison.Ordinal);
        if (rootIndex < 0)
        {
            return Array.Empty<RetailVmdlNode>();
        }

        var childrenIndex = text.IndexOf("children", rootIndex, StringComparison.Ordinal);
        if (childrenIndex < 0)
        {
            return Array.Empty<RetailVmdlNode>();
        }

        var arrayStart = text.IndexOf('[', childrenIndex);
        if (arrayStart < 0)
        {
            return Array.Empty<RetailVmdlNode>();
        }

        var arrayEnd = FindMatching(text, arrayStart, '[', ']');
        if (arrayEnd < 0)
        {
            throw new InvalidDataException("Retail VMDL root children array is unbalanced.");
        }

        var nodes = new List<RetailVmdlNode>();
        var cursor = arrayStart + 1;

        while (cursor < arrayEnd)
        {
            cursor = SkipWhitespaceAndCommas(text, cursor, arrayEnd);
            if (cursor >= arrayEnd)
            {
                break;
            }

            if (text[cursor] != '{')
            {
                cursor++;
                continue;
            }

            var nodeEnd = FindMatching(text, cursor, '{', '}');
            if (nodeEnd < 0 || nodeEnd > arrayEnd)
            {
                throw new InvalidDataException("Retail VMDL contains an unbalanced root child node.");
            }

            var nodeText = text[cursor..(nodeEnd + 1)];
            var classMatch = ClassRegex.Match(nodeText);
            if (classMatch.Success)
            {
                nodes.Add(new RetailVmdlNode(classMatch.Groups["class"].Value, nodeText));
            }

            cursor = nodeEnd + 1;
        }

        return nodes;
    }

    private static int SkipWhitespaceAndCommas(string text, int index, int limit)
    {
        while (index < limit && (char.IsWhiteSpace(text[index]) || text[index] == ','))
        {
            index++;
        }

        return index;
    }

    private static int FindMatching(string text, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string ToSourceVmdlResourcePath(string compiledResourcePath)
    {
        var normalized = compiledResourcePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Retail main model is not a .vmdl_c resource: {compiledResourcePath}");
        }

        return normalized[..^2];
    }
}
