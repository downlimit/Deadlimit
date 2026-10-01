using Datamodel;
using Datamodel.Codecs;

namespace Deadlimit.Core;

public sealed record DmxSyntheticRootRepairResult(
    bool Repaired,
    int JointCountBefore,
    int JointCountAfter);

public static class DmxSyntheticRootRepairService
{
    public static DmxSyntheticRootRepairResult Repair(string dmxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dmxPath);

        var temporaryPath = dmxPath + $".deadlimit-root-repair-{Guid.NewGuid():N}.tmp";
        try
        {
            using var document = Datamodel.Datamodel.Load(dmxPath, DeferredMode.Disabled);
            var result = RepairDocument(document);
            if (!result.Repaired)
            {
                return result;
            }

            document.Save(temporaryPath, document.Encoding, document.EncodingVersion);
            File.Move(temporaryPath, dmxPath, overwrite: true);
            return result;
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
                // A cleanup failure must not invalidate a completed staged-DMX repair.
            }
        }
    }

    internal static DmxSyntheticRootRepairResult RepairDocument(Datamodel.Datamodel document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var models = document.AllElements.Where(element =>
                string.Equals(element.ClassName, "DmeModel", StringComparison.Ordinal)
                && element.ContainsKey("jointList")
                && element.ContainsKey("children"))
            .Take(2)
            .ToArray();
        if (models.Length != 1)
        {
            return new DmxSyntheticRootRepairResult(false, 0, 0);
        }

        var model = models[0];

        var jointList = model.GetArray<Element>("jointList");
        var modelChildren = model.GetArray<Element>("children");
        if (jointList is null || modelChildren is null || jointList.Count < 2)
        {
            return new DmxSyntheticRootRepairResult(false, jointList?.Count ?? 0, jointList?.Count ?? 0);
        }

        var root = jointList[0];
        var rootChildren = root.GetArray<Element>("children");
        if (!string.Equals(root.ClassName, "DmeJoint", StringComparison.Ordinal)
            || !string.Equals(root.Name, "root", StringComparison.OrdinalIgnoreCase)
            || rootChildren is null
            || rootChildren.Count == 0
            || !modelChildren.Contains(root)
            || !rootChildren.Any(child =>
                string.Equals(child.ClassName, "DmeJoint", StringComparison.Ordinal)
                && string.Equals(child.Name, "root_motion", StringComparison.OrdinalIgnoreCase)))
        {
            return new DmxSyntheticRootRepairResult(false, jointList.Count, jointList.Count);
        }

        var rootTransform = root.Get<Element>("transform");
        if (rootTransform is null || document.AllElements.Any(element =>
                string.Equals(element.ClassName, "DmeChannel", StringComparison.Ordinal)
                && ReferenceEquals(element.Get<Element>("toElement"), rootTransform)))
        {
            return new DmxSyntheticRootRepairResult(false, jointList.Count, jointList.Count);
        }

        var jointTransforms = model.GetArray<Element>("jointTransforms");
        if (jointTransforms is null
            || jointTransforms.Count != jointList.Count
            || !string.Equals(jointTransforms[0].Name, "root", StringComparison.OrdinalIgnoreCase))
        {
            return new DmxSyntheticRootRepairResult(false, jointList.Count, jointList.Count);
        }

        var baseStates = model.GetArray<Element>("baseStates");
        var transformLists = baseStates?
            .Where(state => state.ContainsKey("transforms"))
            .Select(state => state.GetArray<Element>("transforms"))
            .Where(transforms => transforms is not null)
            .Select(transforms => transforms!)
            .ToArray() ?? [];
        if (transformLists.Any(transforms =>
                transforms.Count != jointList.Count
                || !string.Equals(transforms[0].Name, "root", StringComparison.OrdinalIgnoreCase)))
        {
            return new DmxSyntheticRootRepairResult(false, jointList.Count, jointList.Count);
        }

        var before = jointList.Count;
        var rootIndex = modelChildren.IndexOf(root);
        modelChildren.RemoveAt(rootIndex);
        for (var index = 0; index < rootChildren.Count; index++)
        {
            modelChildren.Insert(rootIndex + index, rootChildren[index]);
        }

        jointList.RemoveAt(0);
        jointTransforms.RemoveAt(0);
        foreach (var transforms in transformLists)
        {
            transforms.RemoveAt(0);
        }

        return new DmxSyntheticRootRepairResult(true, before, jointList.Count);
    }
}
