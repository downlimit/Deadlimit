using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailVmdlNode(string ClassName, string Text);

public sealed record RetailVmdlInheritanceResult(
    string? SourceVmdlPath,
    IReadOnlyList<RetailVmdlNode> Nodes,
    IReadOnlyList<string> RemovedClasses)
{
    public bool Contains(string className) =>
        Nodes.Any(node => string.Equals(node.ClassName, className, StringComparison.Ordinal));
}

public static class RetailVmdlInheritance
{
    // These lists are current CSDK12-incompatible in source ModelDoc. They are restored
    // only after compilation through DeadlockTools, so they must never be copied into
    // the authoring VMDL.
    private static readonly HashSet<string> UnsupportedSourceClasses = new(StringComparer.Ordinal)
    {
        "NmSkeletonList",
        "AnimGraph2List",
    };

    // Deadlimit owns these nodes because the render mesh comes from the artist project
    // and material-path repair/custom authoring is project-owned.
    private static readonly HashSet<string> GeneratedClasses = new(StringComparer.Ordinal)
    {
        "RenderMeshList",
        "MaterialGroupList",
    };

    private static readonly Regex ClassRegex = new(
        "\\b_class\\s*=\\s*\"(?<class>[^\"]+)\"",
        RegexOptions.Compiled);

    public static RetailVmdlInheritanceResult Load(ProjectManifest manifest)
    {
        var sourceVmdl = FindRetailVmdl(manifest);
        if (sourceVmdl is null)
        {
            return new RetailVmdlInheritanceResult(
                null,
                Array.Empty<RetailVmdlNode>(),
                Array.Empty<string>());
        }

        var text = File.ReadAllText(sourceVmdl);
        var allNodes = ExtractRootChildNodes(text);

        var removedClasses = allNodes
            .Where(node => UnsupportedSourceClasses.Contains(node.ClassName))
            .Select(node => node.ClassName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var preservedNodes = allNodes
            .Where(node => !UnsupportedSourceClasses.Contains(node.ClassName)
                && !GeneratedClasses.Contains(node.ClassName))
            .ToArray();

        return new RetailVmdlInheritanceResult(sourceVmdl, preservedNodes, removedClasses);
    }

    public static string? FindRetailVmdl(ProjectManifest manifest)
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

    public static int CopyRetailModelSourceTree(ProjectManifest manifest, string addonContentRoot)
    {
        var sourceVmdl = FindRetailVmdl(manifest);
        if (sourceVmdl is null || string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            return 0;
        }

        var sourceFolder = Path.GetDirectoryName(sourceVmdl)!;
        var resourceFolder = Path.GetDirectoryName(ToSourceVmdlResourcePath(manifest.RetailMainModel).Replace('/', Path.DirectorySeparatorChar))
            ?? string.Empty;
        var destinationFolder = Path.Combine(addonContentRoot, resourceFolder);

        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, sourceFile);
            var destination = Path.Combine(destinationFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
            copied++;
        }

        return copied;
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
