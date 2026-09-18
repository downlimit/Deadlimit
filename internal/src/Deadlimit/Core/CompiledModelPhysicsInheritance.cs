using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

internal sealed record CompiledPhysicsInheritanceResult(
    int RetailNodeCount,
    int CustomJiggleCount,
    int MergedNodeCount,
    int RetailRigidBodyCount,
    bool PreservedAuthoredCloth);

internal static class CompiledModelPhysicsInheritance
{
    private const string FeModelField = "m_pFeModel";

    private static readonly HashSet<string> NodeReferenceFields = new(StringComparer.Ordinal)
    {
        "nNode",
        "nNodeX0",
        "nNodeX1",
        "nNodeY0",
        "nNodeY1",
        "nNodeOrient",
        "nNodeEnd",
        "nCtrlParent",
        "nCtrlChild",
        "nBoneCtrl",
        "nTargetNode",
        "m_nNode",
        "m_nJiggleParent",
        "m_AntiTunnelTargetNodes",
        "m_WorldCollisionNodes",
        "m_LockToParent",
        "m_LockToGoal",
        "m_SkelParents",
    };

    private static readonly string[] PerNodeArrays =
    [
        "m_CtrlHash",
        "m_CtrlName",
        "m_InitPose",
        "m_NodeInvMasses",
        "m_NodeIntegrator",
        "m_SkelParents",
    ];

    // ClothChain authoring compiles into general FE constraints rather than
    // m_JiggleBones. Those constraints form one interdependent FE graph and
    // cannot be safely spliced into the retail graph node-by-node.
    private static readonly string[] AuthoredClothTopologyFields =
    [
        "m_AxialEdges",
        "m_GoalDampedSpringIntegrators",
        "m_HingeLimits",
        "m_KelagerBends",
        "m_Quads",
        "m_Rods",
        "m_Ropes",
        "m_SimdQuads",
        "m_SimdRods",
        "m_SimdRodsAnim",
        "m_SimdSpringIntegrator",
        "m_SimdTris",
        "m_SpringIntegrator",
        "m_Tris",
        "m_Twists",
    ];

    public static CompiledPhysicsInheritanceResult Apply(
        ProjectManifest manifest,
        string compiledModelPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(compiledModelPath);

        if (string.IsNullOrWhiteSpace(manifest.RetailSourceVpk) || !File.Exists(manifest.RetailSourceVpk))
        {
            throw new FileNotFoundException(
                "The retail VPK recorded by hero extraction was not found. Refresh hero source before building.",
                manifest.RetailSourceVpk);
        }
        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("The retail main model is unknown.");
        }

        var resourcePath = NormalizeResourcePath(manifest.RetailMainModel);
        var retailBytes = ReadRetailResource(manifest.RetailSourceVpk, resourcePath);
        var compiledBytes = File.ReadAllBytes(compiledModelPath);

        using var retailStream = new MemoryStream(retailBytes, writable: false);
        using var compiledStream = new MemoryStream(compiledBytes, writable: false);
        using var retailResource = ReadModel(retailStream, resourcePath);
        using var compiledResource = ReadModel(compiledStream, resourcePath);

        var retailPhysics = GetPhysics(retailResource, "Retail");
        var compiledPhysics = GetPhysics(compiledResource, "Compiled");
        var retailFe = GetFeModel(retailPhysics, "Retail");
        var compiledFe = GetFeModel(compiledPhysics, "Compiled");
        var preserveAuthoredCloth = HasAuthoredClothTopology(compiledFe);
        var merge = preserveAuthoredCloth
            ? PreserveCompiledFe(compiledFe)
            : Merge(retailFe, compiledFe);

        RestoreRetailRigidBodies(retailPhysics, compiledPhysics);
        compiledPhysics.Data[FeModelField] = merge.Model;
        using var physicsOutput = new MemoryStream();
        compiledPhysics.Serialize(physicsOutput);
        var repairedBytes = CompiledResourceBlockRewriter.Replace(
            compiledBytes,
            "PHYS",
            physicsOutput.ToArray());

        Verify(repairedBytes, resourcePath, merge);
        WriteAtomically(compiledModelPath, repairedBytes);
        return new CompiledPhysicsInheritanceResult(
            GetInt32(retailFe, "m_nNodeCount"),
            merge.CustomNames.Count,
            merge.MergedNodeCount,
            GetArray(retailPhysics.Data, "m_parts").Count,
            preserveAuthoredCloth);
    }

    internal static KVObject MergeFeModelsForSmoke(KVObject retail, KVObject compiled) =>
        Merge(retail, compiled).Model;

    internal static bool HasAuthoredClothTopologyForSmoke(KVObject compiled) =>
        HasAuthoredClothTopology(compiled);

    internal static KVObject SelectFeModelForSmoke(KVObject retail, KVObject compiled) =>
        (HasAuthoredClothTopology(compiled)
            ? PreserveCompiledFe(compiled)
            : Merge(retail, compiled)).Model;

    internal static KVObject RestoreRetailRigidBodiesForSmoke(
        KVObject retailPhysics,
        KVObject compiledPhysics)
    {
        RestoreRetailRigidBodies(retailPhysics, compiledPhysics);
        return compiledPhysics;
    }

    private static FeMergeResult PreserveCompiledFe(KVObject compiled)
    {
        var nodeCount = GetInt32(compiled, "m_nNodeCount");
        return new FeMergeResult(
            Clone(compiled),
            0,
            nodeCount,
            ReadNames(compiled));
    }

    private static bool HasAuthoredClothTopology(KVObject compiled) =>
        AuthoredClothTopologyFields.Any(field =>
            compiled.TryGetValue(field, out var value)
            && value.IsArray
            && value.Count > 0);

    private static void RestoreRetailRigidBodies(
        PhysAggregateData retail,
        PhysAggregateData compiled) =>
        RestoreRetailRigidBodies(retail.Data, compiled.Data);

    private static void RestoreRetailRigidBodies(KVObject retail, KVObject compiled)
    {
        foreach (var item in retail)
        {
            if (!string.Equals(item.Key, FeModelField, StringComparison.Ordinal))
            {
                compiled[item.Key] = Clone(item.Value);
            }
        }
    }

    private static FeMergeResult Merge(KVObject retail, KVObject compiled)
    {
        var retailNodeCount = GetInt32(retail, "m_nNodeCount");
        var retailStaticNodes = GetInt32(retail, "m_nStaticNodes");
        var insertionNode = GetInt32(retail, "m_nFirstPositionDrivenNode");
        if (retailNodeCount <= 0 || insertionNode < retailStaticNodes || insertionNode > retailNodeCount)
        {
            throw new InvalidDataException("Retail FE model has an invalid node layout.");
        }

        var retailNames = ReadNames(retail);
        var retailNameSet = retailNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var custom = ExtractCustomJiggles(compiled, retailNameSet);
        var customCount = custom.Count;
        var merged = CloneAndRemap(retail, insertionNode, retailNodeCount, customCount);

        if (customCount == 0)
        {
            return new FeMergeResult(merged, retailNodeCount, retailNodeCount, Array.Empty<string>());
        }

        var customNodeMap = custom
            .Select((item, index) => (item.OriginalNode, NewNode: insertionNode + index))
            .ToDictionary(pair => pair.OriginalNode, pair => pair.NewNode);

        foreach (var field in PerNodeArrays)
        {
            var customValues = custom.Select(item => field switch
            {
                "m_CtrlHash" => item.CtrlHash,
                "m_CtrlName" => item.CtrlName,
                "m_InitPose" => item.InitPose,
                "m_NodeInvMasses" => item.NodeInvMass,
                "m_NodeIntegrator" => item.NodeIntegrator,
                "m_SkelParents" => RemapCustomParent(item.SkelParent, customNodeMap),
                _ => throw new UnreachableException(),
            });
            merged[field] = SpliceArray(
                retail[field],
                insertionNode,
                customValues,
                value => CloneAndRemap(value, insertionNode, retailNodeCount, customCount, field));
        }

        var dynamicInsertion = insertionNode - retailStaticNodes;
        merged["m_DynNodeWindBases"] = SpliceArray(
            retail["m_DynNodeWindBases"],
            dynamicInsertion,
            custom.Select(item => item.WindBase),
            value => CloneAndRemap(value, insertionNode, retailNodeCount, customCount));
        merged["m_NodeCollisionRadii"] = SpliceArray(
            retail["m_NodeCollisionRadii"],
            dynamicInsertion,
            custom.Select(_ => new KVObject(0f)),
            Clone);
        merged["m_DynNodeFriction"] = SpliceArray(
            retail["m_DynNodeFriction"],
            dynamicInsertion,
            custom.Select(_ => new KVObject(0f)),
            Clone);

        if (retail["m_SourceElems"].IsArray && retail["m_SourceElems"].Count == retailNodeCount + 1)
        {
            var sourceType = retail["m_SourceElems"].Values.FirstOrDefault() ?? new KVObject(0);
            merged["m_SourceElems"] = SpliceArray(
                retail["m_SourceElems"],
                insertionNode,
                custom.Select(_ => NumericLike(sourceType, 0)),
                Clone);
        }

        var jiggles = KVObject.Array(0);
        foreach (var item in custom)
        {
            var jiggle = Clone(item.Jiggle);
            jiggle["m_nNode"] = NumericLike(item.Jiggle["m_nNode"], customNodeMap[item.OriginalNode]);
            var oldParent = ToUInt64(item.Jiggle["m_nJiggleParent"]);
            if (oldParent != uint.MaxValue)
            {
                if (!customNodeMap.TryGetValue(checked((int)oldParent), out var newParent))
                {
                    throw new InvalidDataException(
                        $"Custom jiggle '{item.Name}' depends on an FE node that cannot be inherited safely.");
                }
                jiggle["m_nJiggleParent"] = NumericLike(item.Jiggle["m_nJiggleParent"], newParent);
            }
            jiggles.Add(jiggle);
        }
        merged["m_JiggleBones"] = jiggles;

        MergeCollisionTree(
            retail,
            merged,
            dynamicInsertion,
            customCount);

        merged["m_nNodeCount"] = NumericLike(retail["m_nNodeCount"], retailNodeCount + customCount);
        merged["m_nFirstPositionDrivenNode"] = NumericLike(
            retail["m_nFirstPositionDrivenNode"],
            insertionNode + customCount);
        merged["m_nDynamicNodeFlags"] = NumericLike(
            retail["m_nDynamicNodeFlags"],
            ToUInt64(retail["m_nDynamicNodeFlags"]) | ToUInt64(compiled["m_nDynamicNodeFlags"]));

        return new FeMergeResult(
            merged,
            retailNodeCount,
            retailNodeCount + customCount,
            custom.Select(item => item.Name).ToArray());
    }

    private static IReadOnlyList<CustomJiggle> ExtractCustomJiggles(
        KVObject compiled,
        IReadOnlySet<string> retailNames)
    {
        var names = ReadNames(compiled);
        var customNodes = names
            .Select((name, index) => (Name: name, Node: index))
            .Where(pair => !retailNames.Contains(pair.Name))
            .ToArray();
        if (customNodes.Length == 0)
        {
            return Array.Empty<CustomJiggle>();
        }

        var jigglesByNode = GetArray(compiled, "m_JiggleBones")
            .Values
            .ToDictionary(
                item => GetInt32(item, "m_nNode"),
                item => item);
        var compiledStaticNodes = GetInt32(compiled, "m_nStaticNodes");
        var result = new List<CustomJiggle>(customNodes.Length);
        foreach (var (name, node) in customNodes)
        {
            if (!jigglesByNode.TryGetValue(node, out var jiggle))
            {
                throw new InvalidDataException(
                    $"Custom FE node '{name}' is not a jiggle bone. This physics type cannot yet be merged with retail FE physics without data loss.");
            }
            var dynamicIndex = node - compiledStaticNodes;
            if (dynamicIndex < 0 || dynamicIndex >= GetArray(compiled, "m_DynNodeWindBases").Count)
            {
                throw new InvalidDataException($"Custom jiggle '{name}' has an invalid dynamic-node index.");
            }

            result.Add(new CustomJiggle(
                name,
                node,
                Clone(GetArray(compiled, "m_CtrlHash")[node]),
                Clone(GetArray(compiled, "m_CtrlName")[node]),
                Clone(GetArray(compiled, "m_InitPose")[node]),
                Clone(GetArray(compiled, "m_NodeInvMasses")[node]),
                Clone(GetArray(compiled, "m_NodeIntegrator")[node]),
                Clone(GetArray(compiled, "m_SkelParents")[node]),
                Clone(GetArray(compiled, "m_DynNodeWindBases")[dynamicIndex]),
                Clone(jiggle)));
        }
        return result;
    }

    private static KVObject RemapCustomParent(KVObject value, IReadOnlyDictionary<int, int> nodeMap)
    {
        var parent = ToInt64(value);
        if (parent < 0)
        {
            return Clone(value);
        }
        if (!nodeMap.TryGetValue(checked((int)parent), out var mapped))
        {
            throw new InvalidDataException("A custom jiggle skeletal parent is outside the inherited custom jiggle set.");
        }
        return NumericLike(value, mapped);
    }

    private static void MergeCollisionTree(
        KVObject retail,
        KVObject merged,
        int insertionLeaf,
        int customLeaves)
    {
        var retailParents = GetArray(retail, "m_TreeParents");
        var retailMasks = GetArray(retail, "m_TreeCollisionMasks");
        var retailChildren = GetArray(retail, "m_TreeChildren");
        if (retailParents.Count == 0 || retailParents.Count != retailMasks.Count)
        {
            throw new InvalidDataException("Retail FE collision tree is missing or malformed.");
        }

        var retailLeaves = (retailParents.Count + 1) / 2;
        if (retailParents.Count != (retailLeaves * 2) - 1
            || retailChildren.Count != retailLeaves - 1
            || insertionLeaf < 0
            || insertionLeaf > retailLeaves)
        {
            throw new InvalidDataException("Retail FE collision tree has an unsupported layout.");
        }

        var totalLeaves = retailLeaves + customLeaves;
        var retailInternalStart = totalLeaves;
        var customInternalStart = retailInternalStart + (retailLeaves - 1);
        var newRoot = (totalLeaves * 2) - 2;
        var retailRoot = MapRetailTreeIndex(retailParents.Count - 1);
        var customParents = BuildSimpleTree(customLeaves);
        var customRoot = MapCustomTreeIndex(customParents.Parents.Count - 1);

        var parentSample = retailParents.Values.First();
        var maskSample = retailMasks.Values.First();
        var parents = Enumerable.Repeat<KVObject?>(null, (totalLeaves * 2) - 1).ToArray();
        var masks = Enumerable.Repeat<KVObject?>(null, parents.Length).ToArray();

        for (var index = 0; index < retailParents.Count; index++)
        {
            var mappedIndex = MapRetailTreeIndex(index);
            var oldParent = ToUInt64(retailParents[index]);
            parents[mappedIndex] = NumericLike(
                parentSample,
                oldParent == ushort.MaxValue ? newRoot : MapRetailTreeIndex(checked((int)oldParent)));
            masks[mappedIndex] = Clone(retailMasks[index]);
        }
        for (var index = 0; index < customParents.Parents.Count; index++)
        {
            var mappedIndex = MapCustomTreeIndex(index);
            var oldParent = customParents.Parents[index];
            parents[mappedIndex] = NumericLike(
                parentSample,
                oldParent < 0 ? newRoot : MapCustomTreeIndex(oldParent));
            masks[mappedIndex] = NumericLike(maskSample, 0);
        }
        parents[newRoot] = NumericLike(parentSample, ushort.MaxValue);
        masks[newRoot] = NumericLike(
            maskSample,
            ToUInt64(retailMasks[retailParents.Count - 1]));

        var children = new List<KVObject>(totalLeaves - 1);
        foreach (var child in retailChildren.Values)
        {
            children.Add(CloneTreeChild(child, MapRetailTreeIndex));
        }
        foreach (var child in customParents.Children)
        {
            children.Add(CreateTreeChild(child.Left, child.Right, MapCustomTreeIndex, retailChildren));
        }
        children.Add(CreateTreeChild(retailRoot, customRoot, value => value, retailChildren));

        merged["m_TreeParents"] = KVObject.Array(parents.Select(value => value!));
        merged["m_TreeCollisionMasks"] = KVObject.Array(masks.Select(value => value!));
        merged["m_TreeChildren"] = KVObject.Array(children);
        merged["m_nTreeDepth"] = NumericLike(
            retail["m_nTreeDepth"],
            Math.Max(GetInt32(retail, "m_nTreeDepth"), customParents.Depth) + 1);

        int MapRetailTreeIndex(int index)
        {
            if (index < retailLeaves)
            {
                return index < insertionLeaf ? index : index + customLeaves;
            }
            return retailInternalStart + (index - retailLeaves);
        }

        int MapCustomTreeIndex(int index) => index < customLeaves
            ? insertionLeaf + index
            : customInternalStart + (index - customLeaves);
    }

    private static SimpleTree BuildSimpleTree(int leafCount)
    {
        if (leafCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leafCount));
        }
        if (leafCount == 1)
        {
            return new SimpleTree([-1], Array.Empty<TreePair>(), 0);
        }

        var parents = Enumerable.Repeat(-1, (leafCount * 2) - 1).ToArray();
        var children = new List<TreePair>(leafCount - 1);
        var queue = new Queue<(int Index, int Depth)>(
            Enumerable.Range(0, leafCount).Select(index => (index, 0)));
        var next = leafCount;
        while (queue.Count > 1)
        {
            var left = queue.Dequeue();
            var right = queue.Dequeue();
            var parent = next++;
            parents[left.Index] = parent;
            parents[right.Index] = parent;
            children.Add(new TreePair(left.Index, right.Index));
            queue.Enqueue((parent, Math.Max(left.Depth, right.Depth) + 1));
        }
        var root = queue.Dequeue();
        return new SimpleTree(parents, children, root.Depth);
    }

    private static KVObject CloneTreeChild(KVObject source, Func<int, int> map)
    {
        var clone = Clone(source);
        var nodes = GetArray(clone, "nChild");
        clone["nChild"] = KVObject.Array(nodes.Values.Select(value =>
            NumericLike(value, map(checked((int)ToUInt64(value))))));
        return clone;
    }

    private static KVObject CreateTreeChild(
        int left,
        int right,
        Func<int, int> map,
        KVObject retailChildren)
    {
        var template = retailChildren.Values.First();
        var templateNodes = GetArray(template, "nChild");
        var result = KVObject.Collection();
        result["nChild"] = KVObject.Array(
        [
            NumericLike(templateNodes[0], map(left)),
            NumericLike(templateNodes[1], map(right)),
        ]);
        result.Flag = template.Flag;
        return result;
    }

    private static KVObject SpliceArray(
        KVObject source,
        int insertionIndex,
        IEnumerable<KVObject> additions,
        Func<KVObject, KVObject> cloneExisting)
    {
        if (!source.IsArray || insertionIndex < 0 || insertionIndex > source.Count)
        {
            throw new InvalidDataException("FE array cannot be spliced at the requested node position.");
        }
        var values = source.Values.ToArray();
        var result = KVObject.Array(
            values.Take(insertionIndex).Select(cloneExisting)
                .Concat(additions.Select(Clone))
                .Concat(values.Skip(insertionIndex).Select(cloneExisting)));
        result.Flag = source.Flag;
        return result;
    }

    private static KVObject CloneAndRemap(
        KVObject source,
        int insertionNode,
        int retailNodeCount,
        int addedNodes,
        string? fieldName = null)
    {
        var remapValues = fieldName is not null && NodeReferenceFields.Contains(fieldName);
        KVObject clone;
        if (source.IsArray)
        {
            clone = KVObject.Array(source.Values.Select(value =>
                CloneAndRemap(value, insertionNode, retailNodeCount, addedNodes, fieldName)));
        }
        else if (source.IsCollection)
        {
            clone = KVObject.Collection();
            foreach (var child in source)
            {
                clone[child.Key] = CloneAndRemap(
                    child.Value,
                    insertionNode,
                    retailNodeCount,
                    addedNodes,
                    child.Key);
            }
        }
        else if (remapValues && IsInteger(source))
        {
            if (source.ValueType is KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64)
            {
                var value = ToInt64(source);
                clone = value >= insertionNode && value < retailNodeCount
                    ? NumericLike(source, value + addedNodes)
                    : Clone(source);
            }
            else
            {
                var value = ToUInt64(source);
                clone = value >= (ulong)insertionNode && value < (ulong)retailNodeCount
                    ? NumericLike(source, value + (ulong)addedNodes)
                    : Clone(source);
            }
        }
        else
        {
            clone = Clone(source);
        }
        clone.Flag = source.Flag;
        return clone;
    }

    private static KVObject Clone(KVObject source)
    {
        KVObject clone = source.ValueType switch
        {
            KVValueType.Null => KVObject.Null(),
            KVValueType.Collection => CloneCollection(source),
            KVValueType.Array => KVObject.Array(source.Values.Select(Clone)),
            KVValueType.Boolean => new KVObject(source.ToBoolean()),
            KVValueType.String => new KVObject((string)source),
            KVValueType.Int16 => new KVObject(source.ToInt16()),
            KVValueType.Int32 => new KVObject(source.ToInt32(CultureInfo.InvariantCulture)),
            KVValueType.Int64 => new KVObject(source.ToInt64(CultureInfo.InvariantCulture)),
            KVValueType.UInt16 => new KVObject(source.ToUInt16()),
            KVValueType.UInt32 => new KVObject(source.ToUInt32(CultureInfo.InvariantCulture)),
            KVValueType.UInt64 => new KVObject(source.ToUInt64(CultureInfo.InvariantCulture)),
            KVValueType.FloatingPoint => new KVObject(source.ToSingle(CultureInfo.InvariantCulture)),
            KVValueType.FloatingPoint64 => new KVObject(source.ToDouble(CultureInfo.InvariantCulture)),
            KVValueType.Pointer => new KVObject((nint)source.ToInt64(CultureInfo.InvariantCulture)),
            _ => throw new InvalidDataException($"Unsupported FE value type: {source.ValueType}."),
        };
        clone.Flag = source.Flag;
        return clone;
    }

    private static KVObject CloneCollection(KVObject source)
    {
        var result = KVObject.Collection();
        foreach (var child in source)
        {
            result[child.Key] = Clone(child.Value);
        }
        return result;
    }

    private static KVObject NumericLike(KVObject template, long value) => template.ValueType switch
    {
        KVValueType.Int16 => new KVObject(checked((short)value)),
        KVValueType.Int32 => new KVObject(checked((int)value)),
        KVValueType.Int64 => new KVObject(value),
        KVValueType.UInt16 => new KVObject(checked((ushort)value)),
        KVValueType.UInt32 => new KVObject(checked((uint)value)),
        KVValueType.UInt64 => new KVObject(checked((ulong)value)),
        _ => throw new InvalidDataException($"Expected an integer FE value, found {template.ValueType}."),
    };

    private static KVObject NumericLike(KVObject template, ulong value) => template.ValueType switch
    {
        KVValueType.Int16 => new KVObject(checked((short)value)),
        KVValueType.Int32 => new KVObject(checked((int)value)),
        KVValueType.Int64 => new KVObject(checked((long)value)),
        KVValueType.UInt16 => new KVObject(checked((ushort)value)),
        KVValueType.UInt32 => new KVObject(checked((uint)value)),
        KVValueType.UInt64 => new KVObject(value),
        _ => throw new InvalidDataException($"Expected an integer FE value, found {template.ValueType}."),
    };

    private static bool IsInteger(KVObject value) => value.ValueType is
        KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64
        or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64;

    private static int GetInt32(KVObject value, string field) =>
        value[field].ToInt32(CultureInfo.InvariantCulture);

    private static long ToInt64(KVObject value) => value.ToInt64(CultureInfo.InvariantCulture);

    private static ulong ToUInt64(KVObject value) => value.ToUInt64(CultureInfo.InvariantCulture);

    private static KVObject GetArray(KVObject value, string field)
    {
        var array = value[field];
        if (!array.IsArray)
        {
            throw new InvalidDataException($"FE field {field} is not an array.");
        }
        return array;
    }

    private static string[] ReadNames(KVObject fe) => GetArray(fe, "m_CtrlName")
        .Values
        .Select(value => (string)value)
        .ToArray();

    private static PhysAggregateData GetPhysics(Resource resource, string label) =>
        (resource.DataBlock as Model)?.GetEmbeddedPhys()
        ?? throw new InvalidDataException($"{label} model contains no embedded PHYS block.");

    private static KVObject GetFeModel(PhysAggregateData physics, string label)
    {
        if (!physics.Data.TryGetValue(FeModelField, out var fe) || !fe.IsCollection)
        {
            throw new InvalidDataException($"{label} PHYS block contains no FE model.");
        }
        return fe;
    }

    private static Resource ReadModel(Stream stream, string resourcePath)
    {
        var resource = new Resource { FileName = resourcePath };
        try
        {
            resource.Read(stream, verifyFileSize: true, leaveOpen: true);
            if (resource.ResourceType != ResourceType.Model || resource.DataBlock is not Model)
            {
                throw new InvalidDataException($"Resource is not a compiled model: {resourcePath}");
            }
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    private static byte[] ReadRetailResource(string vpkPath, string resourcePath)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var entry = (package.Entries ?? throw new InvalidDataException($"Retail VPK contains no entries: {vpkPath}"))
            .SelectMany(group => group.Value)
            .FirstOrDefault(candidate => string.Equals(
                NormalizeResourcePath(candidate.GetFullPath()),
                resourcePath,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Retail model '{resourcePath}' was not found in {vpkPath}.");
        package.ReadEntry(entry, out byte[] bytes);
        return bytes;
    }

    private static void Verify(byte[] bytes, string resourcePath, FeMergeResult expected)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var resource = ReadModel(stream, resourcePath);
        var fe = GetFeModel(GetPhysics(resource, "Repaired"), "Repaired");
        var names = ReadNames(fe);
        if (GetInt32(fe, "m_nNodeCount") != expected.MergedNodeCount
            || names.Length != expected.MergedNodeCount
            || expected.CustomNames.Any(name => !names.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Serialized PHYS verification did not preserve retail FE physics and custom jiggles.");
        }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + $".deadlimit-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private sealed record CustomJiggle(
        string Name,
        int OriginalNode,
        KVObject CtrlHash,
        KVObject CtrlName,
        KVObject InitPose,
        KVObject NodeInvMass,
        KVObject NodeIntegrator,
        KVObject SkelParent,
        KVObject WindBase,
        KVObject Jiggle);

    private sealed record FeMergeResult(
        KVObject Model,
        int RetailNodeCount,
        int MergedNodeCount,
        IReadOnlyList<string> CustomNames);

    private sealed record SimpleTree(
        IReadOnlyList<int> Parents,
        IReadOnlyList<TreePair> Children,
        int Depth);

    private sealed record TreePair(int Left, int Right);
}

internal static class CompiledResourceBlockRewriter
{
    private const int HeaderSize = 16;
    private const int BlockEntrySize = 12;

    public static byte[] Replace(byte[] resource, string blockType, byte[] replacement)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(replacement);
        if (blockType.Length != 4)
        {
            throw new ArgumentException("Compiled resource block type must contain four ASCII characters.", nameof(blockType));
        }
        if (resource.Length < HeaderSize)
        {
            throw new InvalidDataException("Compiled resource header is truncated.");
        }

        var span = resource.AsSpan();
        var tableOffset = checked(8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(span[8..12]));
        var blockCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..16]));
        if (tableOffset < HeaderSize || tableOffset + (blockCount * BlockEntrySize) > resource.Length)
        {
            throw new InvalidDataException("Compiled resource block table is malformed.");
        }

        var wanted = Encoding.ASCII.GetBytes(blockType);
        var entries = new List<BlockEntry>(blockCount);
        for (var index = 0; index < blockCount; index++)
        {
            var entryOffset = tableOffset + (index * BlockEntrySize);
            var absoluteOffset = checked(entryOffset + 4
                + (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(entryOffset + 4)..(entryOffset + 8)]));
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[(entryOffset + 8)..(entryOffset + 12)]));
            if (absoluteOffset < 0 || size < 0 || absoluteOffset + size > resource.Length)
            {
                throw new InvalidDataException("Compiled resource block points outside the file.");
            }
            entries.Add(new BlockEntry(entryOffset, absoluteOffset, size));
        }

        var targetIndex = -1;
        for (var index = 0; index < blockCount; index++)
        {
            if (span.Slice(tableOffset + (index * BlockEntrySize), 4).SequenceEqual(wanted))
            {
                targetIndex = index;
                break;
            }
        }
        if (targetIndex < 0)
        {
            throw new InvalidDataException($"Compiled resource contains no {blockType} block.");
        }

        var target = entries[targetIndex];
        var oldNextOffset = entries
            .Where(entry => entry.AbsoluteOffset > target.AbsoluteOffset)
            .Select(entry => entry.AbsoluteOffset)
            .DefaultIfEmpty(resource.Length)
            .Min();
        var newNextOffset = Align16(checked(target.AbsoluteOffset + replacement.Length));
        var delta = newNextOffset - oldNextOffset;
        var output = new byte[checked(resource.Length + delta)];
        Buffer.BlockCopy(resource, 0, output, 0, target.AbsoluteOffset);
        Buffer.BlockCopy(replacement, 0, output, target.AbsoluteOffset, replacement.Length);
        Buffer.BlockCopy(resource, oldNextOffset, output, newNextOffset, resource.Length - oldNextOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), checked((uint)output.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(target.EntryOffset + 8, 4),
            checked((uint)replacement.Length));
        foreach (var entry in entries.Where(entry => entry.AbsoluteOffset >= oldNextOffset))
        {
            var newAbsolute = checked(entry.AbsoluteOffset + delta);
            var relative = checked(newAbsolute - (entry.EntryOffset + 4));
            BinaryPrimitives.WriteUInt32LittleEndian(
                output.AsSpan(entry.EntryOffset + 4, 4),
                checked((uint)relative));
        }
        return output;
    }

    private static int Align16(int value) => checked((value + 15) & ~15);

    private sealed record BlockEntry(int EntryOffset, int AbsoluteOffset, int Size);
}
