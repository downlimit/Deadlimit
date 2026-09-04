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
            // A render-mesh DMX may contain a DmeModel root without a skeleton.
            // Datamodel.NET throws for a missing attribute, so check before reading it.
            if (!model.ContainsKey("jointList"))
            {
                continue;
            }

            var joints = model.GetArray<Element>("jointList");
            if (joints is null)
            {
                continue;
            }

            foreach (var joint in joints)
            {
                // Wall Worm may include ordinary render DmeDag nodes in jointList
                // when a Skin modifier is present. Only real DmeJoint nodes are
                // skeleton helpers; excluding every listed node hides render meshes
                // from Vertex Color detection and transfer.
                if (!string.Equals(joint.ClassName, "DmeJoint", StringComparison.Ordinal)
                    || !joint.ContainsKey("shape"))
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
