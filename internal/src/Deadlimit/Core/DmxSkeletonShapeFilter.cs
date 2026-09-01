using Datamodel;

namespace Deadlimit.Core;

internal static class DmxSkeletonShapeFilter
{
    public static HashSet<string> FindJointShapeMeshIds(Datamodel.Datamodel document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeModel", StringComparison.Ordinal)))
        {
            var joints = model.GetArray<Element>("jointList");
            if (joints is null)
            {
                continue;
            }

            foreach (var joint in joints)
            {
                if (!joint.ContainsKey("shape"))
                {
                    continue;
                }

                var shape = joint.Get<Element>("shape");
                if (shape is null
                    || !string.Equals(shape.ClassName, "DmeMesh", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(shape.ID.ToString());
            }
        }

        return result;
    }

    public static bool IsJointShape(Element mesh, IReadOnlySet<string> jointShapeMeshIds)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(jointShapeMeshIds);
        return jointShapeMeshIds.Contains(mesh.ID.ToString());
    }
}
