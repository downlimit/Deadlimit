using Datamodel;

namespace Deadlimit.Core;

public static class DmxSyntheticRootRepairSmoke
{
    public static void Run()
    {
        using var document = new Datamodel.Datamodel("model", 22);
        var model = new Element(document, "model", null, "DmeModel");
        document.Root = model;

        var rootTransform = CreateTransform(document, "root");
        var clothTransform = CreateTransform(document, "_cloth_test");
        var motionTransform = CreateTransform(document, "root_motion");
        var root = CreateJoint(document, "root", rootTransform);
        var cloth = CreateJoint(document, "_cloth_test", clothTransform);
        var rootMotion = CreateJoint(document, "root_motion", motionTransform);
        root.GetArray<Element>("children")!.Add(cloth);
        root.GetArray<Element>("children")!.Add(rootMotion);

        model["children"] = new ElementArray([root]);
        model["jointList"] = new ElementArray([root, cloth, rootMotion]);
        model["jointTransforms"] = new ElementArray([rootTransform, clothTransform, motionTransform]);

        var bind = new Element(document, "bind", null, "DmeTransformList");
        bind["transforms"] = new ElementArray([rootTransform, clothTransform, motionTransform]);
        model["baseStates"] = new ElementArray([bind]);

        var result = DmxSyntheticRootRepairService.RepairDocument(document);
        if (!result.Repaired || result.JointCountBefore != 3 || result.JointCountAfter != 2)
        {
            throw new InvalidOperationException("Synthetic root repair did not report the expected joint-list change.");
        }

        var jointList = model.GetArray<Element>("jointList")!;
        var jointTransforms = model.GetArray<Element>("jointTransforms")!;
        var children = model.GetArray<Element>("children")!;
        var bindTransforms = bind.GetArray<Element>("transforms")!;
        if (jointList.Select(item => item.Name).SequenceEqual(["_cloth_test", "root_motion"]) is false
            || jointTransforms.Select(item => item.Name).SequenceEqual(["_cloth_test", "root_motion"]) is false
            || bindTransforms.Select(item => item.Name).SequenceEqual(["_cloth_test", "root_motion"]) is false
            || children.Select(item => item.Name).SequenceEqual(["_cloth_test", "root_motion"]) is false)
        {
            throw new InvalidOperationException("Synthetic root repair did not keep joint and transform arrays aligned.");
        }
    }

    private static Element CreateTransform(Datamodel.Datamodel document, string name) =>
        new(document, name, null, "DmeTransform");

    private static Element CreateJoint(
        Datamodel.Datamodel document,
        string name,
        Element transform)
    {
        var joint = new Element(document, name, null, "DmeJoint");
        joint["transform"] = transform;
        joint["children"] = new ElementArray();
        return joint;
    }
}
