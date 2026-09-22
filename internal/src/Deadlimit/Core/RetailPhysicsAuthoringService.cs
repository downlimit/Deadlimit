using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Datamodel;
using Datamodel.Codecs;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed record RetailPhysicsInitializationResult(
    int JointCount,
    int ClothChainCount,
    bool Added,
    IReadOnlyList<string> Warnings);

public sealed record ClothBoneNameReconciliationResult(
    int ArtistJointCount,
    int RewrittenReferenceCount,
    IReadOnlyDictionary<string, string> BoneRemaps,
    int RemovedIncompatibleChainCount,
    IReadOnlyList<string> RemovedIncompatibleChainRoots);

public static class RetailPhysicsAuthoringService
{
    private const string PhysicsJointListClass = "PhysicsJointList";
    private const string SoftbodyClass = "Softbody";
    private static readonly Regex ClothChainClassRegex = new(
        "_class\\s*=\\s*\"ClothChain\"",
        RegexOptions.Compiled);
    private static readonly Regex ClothJointNameRegex = new(
        "\\bjoint_name\\s*=\\s*\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled);
    private static readonly Regex ClothJointParentRegex = new(
        "\\bjoint_parent\\s*=\\s*\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled);
    private static readonly Regex ClothRootBoneRegex = new(
        "\\broot_bone\\s*=\\s*\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled);
    private static readonly Regex ClothJointsArrayRegex = new(
        "(?m)^(?<indent>[ \\t]*)joints\\s*=\\s*\\[",
        RegexOptions.Compiled);
    private static readonly Regex ClothBoneReferenceRegex = new(
        "(?<prefix>\\b(?:root_bone|joint_name|joint_parent)\\s*=\\s*\\\")(?<name>\\$cloth_[^\\\"]+)(?<suffix>\\\")",
        RegexOptions.Compiled);

    public static RetailPhysicsInitializationResult EnsureRetailJoints(
        ProjectManifest manifest,
        string destinationVmdlPath,
        bool replaceExisting)
    {
        var addJoints = replaceExisting
            || !RetailVmdlInheritance.ContainsRootNode(destinationVmdlPath, PhysicsJointListClass);
        var addSoftbody = replaceExisting
            || !RetailVmdlInheritance.ContainsRootNode(destinationVmdlPath, SoftbodyClass);
        if (!addJoints && !addSoftbody)
        {
            return new RetailPhysicsInitializationResult(0, 0, false, Array.Empty<string>());
        }

        if (string.IsNullOrWhiteSpace(manifest.RetailSourceVpk)
            || !File.Exists(manifest.RetailSourceVpk))
        {
            throw new FileNotFoundException(
                "The retail VPK recorded by hero extraction was not found. Refresh hero source before initializing physics.",
                manifest.RetailSourceVpk);
        }

        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("The retail main model is unknown.");
        }

        var compiledResourcePath = NormalizeResourcePath(manifest.RetailMainModel);
        using var package = new Package();
        package.Read(manifest.RetailSourceVpk);
        var entry = (package.Entries ?? throw new InvalidDataException(
                $"Retail VPK contains no entries: {manifest.RetailSourceVpk}"))
            .SelectMany(group => group.Value)
            .FirstOrDefault(candidate => string.Equals(
                NormalizeResourcePath(candidate.GetFullPath()),
                compiledResourcePath,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException(
                $"Retail model '{compiledResourcePath}' was not found in {manifest.RetailSourceVpk}.");

        package.ReadEntry(entry, out byte[] rawData);
        using var stream = new MemoryStream(rawData, writable: false);
        using var resource = new Resource { FileName = entry.GetFullPath() };
        resource.Read(stream);

        if (resource.DataBlock is not Model model)
        {
            throw new InvalidDataException($"Retail resource is not a model: {compiledResourcePath}");
        }

        var physics = model.GetEmbeddedPhys()
            ?? throw new InvalidDataException($"Retail model contains no embedded PHYS block: {compiledResourcePath}");
        var jointData = physics.Data["m_joints"];
        if (!jointData.IsArray || jointData.Count == 0)
        {
            throw new InvalidDataException($"Retail model contains no ragdoll joints: {compiledResourcePath}");
        }

        var joints = jointData.Values
            .Select(value => ReadPhysicsJoint(value, physics.BoneNames))
            .ToArray();
        if (addJoints)
        {
            var nodeText = CreatePhysicsJointList(joints);
            RetailVmdlInheritance.UpsertRootNode(destinationVmdlPath, PhysicsJointListClass, nodeText);
        }

        var cloth = physics.Data.TryGetValue("m_pFeModel", out var feModel)
            ? ReadClothChains(feModel)
            : RetailClothReadResult.Empty;
        if (addSoftbody && cloth.Chains.Count > 0)
        {
            RetailVmdlInheritance.UpsertRootNode(
                destinationVmdlPath,
                SoftbodyClass,
                CreateSoftbody(cloth.Chains));
        }

        return new RetailPhysicsInitializationResult(
            addJoints ? joints.Length : 0,
            addSoftbody ? cloth.Chains.Count : 0,
            true,
            cloth.Warnings);
    }

    public static int RepairInvalidClothParentAnchors(string vmdlPath)
    {
        var original = File.ReadAllText(vmdlPath);
        var repairs = new List<(int Start, int Length, string Text)>();

        foreach (Match classMatch in ClothChainClassRegex.Matches(original))
        {
            var blockStart = original.LastIndexOf('{', classMatch.Index);
            if (blockStart < 0)
            {
                continue;
            }

            var blockEnd = FindMatchingBrace(original, blockStart);
            if (blockEnd < 0)
            {
                continue;
            }

            var block = original[blockStart..(blockEnd + 1)];
            if (ClothChainClassRegex.Matches(block).Count != 1)
            {
                // Parent ClothChain containers are repaired through their leaf children.
                continue;
            }

            var nodeNames = ClothJointNameRegex.Matches(block)
                .Select(match => match.Groups["name"].Value)
                .ToArray();
            var parentNames = ClothJointParentRegex.Matches(block)
                .Select(match => (string?)match.Groups["name"].Value)
                .ToArray();
            var missingParents = FindMissingParentJointNames(nodeNames, parentNames);
            if (missingParents.Length == 0)
            {
                continue;
            }

            var repaired = InsertFixedAnchorsIntoClothChain(block, missingParents);
            if (!string.Equals(block, repaired, StringComparison.Ordinal))
            {
                repairs.Add((blockStart, block.Length, repaired));
            }
        }

        if (repairs.Count == 0)
        {
            return 0;
        }

        var patched = new StringBuilder(original);
        foreach (var repair in repairs.OrderByDescending(item => item.Start))
        {
            patched.Remove(repair.Start, repair.Length);
            patched.Insert(repair.Start, repair.Text);
        }
        File.WriteAllText(vmdlPath, patched.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return repairs.Count;
    }


    public static ClothBoneNameReconciliationResult ReconcileWallWormClothBoneNames(
        string vmdlPath,
        IEnumerable<string> preparedDmxPaths,
        IEnumerable<string> preparedFbxPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmdlPath);
        ArgumentNullException.ThrowIfNull(preparedDmxPaths);
        ArgumentNullException.ThrowIfNull(preparedFbxPaths);

        var artistJointNames = ReadPreparedArtistJointNames(preparedDmxPaths, preparedFbxPaths);
        return ReconcileWallWormClothBoneNamesFromJointNames(vmdlPath, artistJointNames);
    }

    internal static ClothBoneNameReconciliationResult ReconcileWallWormClothBoneNamesFromJointNames(
        string vmdlPath,
        IReadOnlySet<string> artistJointNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmdlPath);
        ArgumentNullException.ThrowIfNull(artistJointNames);

        if (artistJointNames.Count == 0)
        {
            return new ClothBoneNameReconciliationResult(
                0,
                0,
                new Dictionary<string, string>(StringComparer.Ordinal),
                0,
                Array.Empty<string>());
        }

        var original = File.ReadAllText(vmdlPath);
        var edits = new List<(int Start, int Length, string Text)>();
        var remaps = new Dictionary<string, string>(StringComparer.Ordinal);
        var removedChainRoots = new List<string>();
        var rewrittenReferenceCount = 0;

        foreach (Match classMatch in ClothChainClassRegex.Matches(original))
        {
            var blockStart = original.LastIndexOf('{', classMatch.Index);
            if (blockStart < 0)
            {
                continue;
            }

            var blockEnd = FindMatchingBrace(original, blockStart);
            if (blockEnd < 0)
            {
                continue;
            }

            var block = original[blockStart..(blockEnd + 1)];
            if (ClothChainClassRegex.Matches(block).Count != 1)
            {
                // Parent ClothChain containers are handled through their leaf children.
                continue;
            }

            var chainRemaps = new Dictionary<string, string>(StringComparer.Ordinal);
            var chainRewriteCount = 0;
            var rewritten = ClothBoneReferenceRegex.Replace(
                block,
                match =>
                {
                    var retailName = match.Groups["name"].Value;
                    if (artistJointNames.Contains(retailName))
                    {
                        return match.Value;
                    }

                    var wallWormName = "_" + retailName[1..];
                    if (!artistJointNames.Contains(wallWormName))
                    {
                        return match.Value;
                    }

                    chainRemaps.TryAdd(retailName, wallWormName);
                    chainRewriteCount++;
                    return match.Groups["prefix"].Value
                           + wallWormName
                           + match.Groups["suffix"].Value;
                });

            // VRF/Wall Worm source DMX uses _cloth_* for procedural cloth bones.
            // If a retail ClothChain still contains $cloth_* after exact aliasing,
            // the corresponding bone is absent from the artist skeleton and the
            // chain cannot compile. Remove that incompatible chain rather than
            // handing ResourceCompiler a guaranteed "Bone ... not found" failure.
            var unresolvedProceduralBones = ClothBoneReferenceRegex.Matches(rewritten)
                .Select(match => match.Groups["name"].Value)
                .Where(name => !artistJointNames.Contains(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (unresolvedProceduralBones.Length > 0)
            {
                var rootMatch = ClothRootBoneRegex.Match(rewritten);
                var rootName = rootMatch.Success
                    ? rootMatch.Groups["name"].Value
                    : unresolvedProceduralBones[0];
                removedChainRoots.Add(rootName);

                var removal = ExpandClothChainRemovalRange(original, blockStart, blockEnd);
                edits.Add((removal.Start, removal.Length, string.Empty));
                continue;
            }

            foreach (var remap in chainRemaps)
            {
                remaps.TryAdd(remap.Key, remap.Value);
            }
            rewrittenReferenceCount += chainRewriteCount;

            if (!string.Equals(block, rewritten, StringComparison.Ordinal))
            {
                edits.Add((blockStart, block.Length, rewritten));
            }
        }

        if (edits.Count > 0)
        {
            var patched = new StringBuilder(original);
            foreach (var edit in edits.OrderByDescending(item => item.Start))
            {
                patched.Remove(edit.Start, edit.Length);
                patched.Insert(edit.Start, edit.Text);
            }

            File.WriteAllText(
                vmdlPath,
                patched.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new ClothBoneNameReconciliationResult(
            artistJointNames.Count,
            rewrittenReferenceCount,
            remaps,
            removedChainRoots.Count,
            removedChainRoots);
    }

    private static (int Start, int Length) ExpandClothChainRemovalRange(
        string text,
        int blockStart,
        int blockEnd)
    {
        var endExclusive = blockEnd + 1;
        while (endExclusive < text.Length
               && (text[endExclusive] == ' ' || text[endExclusive] == '\t'))
        {
            endExclusive++;
        }

        if (endExclusive < text.Length && text[endExclusive] == ',')
        {
            endExclusive++;
        }

        if (endExclusive < text.Length && text[endExclusive] == '\r')
        {
            endExclusive++;
        }
        if (endExclusive < text.Length && text[endExclusive] == '\n')
        {
            endExclusive++;
        }

        return (blockStart, endExclusive - blockStart);
    }

    private static HashSet<string> ReadPreparedArtistJointNames(
        IEnumerable<string> preparedDmxPaths,
        IEnumerable<string> preparedFbxPaths)
    {
        var names = ReadArtistDmxJointNames(preparedDmxPaths);
        foreach (var path in preparedFbxPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var jointName in AsciiFbxVertexColorReader.ReadJointNames(path))
            {
                names.Add(jointName);
            }
        }

        return names;
    }

    private static HashSet<string> ReadArtistDmxJointNames(IEnumerable<string> artistDmxPaths)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in artistDmxPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var document = Datamodel.Datamodel.Load(path, DeferredMode.Disabled);
            foreach (var model in document.AllElements.Where(element =>
                         string.Equals(element.ClassName, "DmeModel", StringComparison.Ordinal)
                         && element.ContainsKey("jointList")))
            {
                var joints = model.GetArray<Element>("jointList");
                if (joints is null)
                {
                    continue;
                }

                foreach (var joint in joints.Where(element =>
                             string.Equals(element.ClassName, "DmeJoint", StringComparison.Ordinal)
                             && !string.IsNullOrWhiteSpace(element.Name)))
                {
                    names.Add(joint.Name!);
                }
            }
        }

        return names;
    }

    private static string InsertFixedAnchorsIntoClothChain(
        string block,
        IReadOnlyList<string> missingParents)
    {
        var jointsMatch = ClothJointsArrayRegex.Match(block);
        if (!jointsMatch.Success)
        {
            return block;
        }

        var newline = block.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertAt = block.IndexOf('\n', jointsMatch.Index + jointsMatch.Length);
        insertAt = insertAt >= 0 ? insertAt + 1 : jointsMatch.Index + jointsMatch.Length;
        var entryIndent = jointsMatch.Groups["indent"].Value + "\t";
        var propertyIndent = entryIndent + "\t";
        var anchors = new StringBuilder();
        foreach (var parent in missingParents)
        {
            anchors.Append(entryIndent).Append('{').Append(newline);
            anchors.Append(propertyIndent).Append("joint_name = \"").Append(EscapeKv3(parent)).Append('"').Append(newline);
            anchors.Append(propertyIndent).Append("simulate = false").Append(newline);
            anchors.Append(propertyIndent).Append("allow_rotation = true").Append(newline);
            anchors.Append(entryIndent).Append("},").Append(newline);
        }

        var repaired = block.Insert(insertAt, anchors.ToString());
        if (missingParents.Count == 1)
        {
            repaired = ClothRootBoneRegex.Replace(
                repaired,
                match =>
                {
                    var nameStart = match.Groups["name"].Index - match.Index;
                    var nameEnd = nameStart + match.Groups["name"].Length;
                    return match.Value[..nameStart]
                           + EscapeKv3(missingParents[0])
                           + match.Value[nameEnd..];
                },
                1);
        }
        return repaired;
    }

    private static int FindMatchingBrace(string text, int openingBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = openingBrace; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (current == '"')
            {
                inString = true;
            }
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}' && --depth == 0)
            {
                return index;
            }
        }
        return -1;
    }

    private static RetailClothReadResult ReadClothChains(KVObject feModel)
    {
        if (!feModel.IsCollection
            || !feModel.TryGetValue("m_CtrlName", out var ctrlNameData)
            || !feModel.TryGetValue("m_SkelParents", out var parentData))
        {
            return RetailClothReadResult.Empty;
        }

        var ctrlNames = ctrlNameData.Values
            .Select(value => value.ToString())
            .ToArray();
        var parents = parentData.Values
            .Select(value => value.ToInt32(CultureInfo.InvariantCulture))
            .ToArray();
        if (ctrlNames.Length == 0 || parents.Length != ctrlNames.Length)
        {
            return RetailClothReadResult.Empty;
        }

        var warnings = FindLossyClothFeatures(feModel).ToList();

        var staticNodeCount = feModel["m_nStaticNodes"].ToInt32(CultureInfo.InvariantCulture);
        var rotationLockedStaticNodeCount = feModel.TryGetValue("m_nRotLockStaticNodes", out var rotationLocks)
            ? rotationLocks.ToInt32(CultureInfo.InvariantCulture)
            : 0;
        var inverseMasses = feModel["m_NodeInvMasses"].Values
            .Select(value => value.ToSingle(CultureInfo.InvariantCulture))
            .ToArray();
        var collisionRadii = feModel["m_NodeCollisionRadii"].Values
            .Select(value => value.ToSingle(CultureInfo.InvariantCulture))
            .ToArray();
        var frictions = feModel["m_DynNodeFriction"].Values
            .Select(value => value.ToSingle(CultureInfo.InvariantCulture))
            .ToArray();
        var integrators = feModel["m_NodeIntegrator"].Values.ToArray();
        var lockedNodes = feModel["m_LockToParent"].Values
            .Select(value => value["nCtrlChild"].ToInt32(CultureInfo.InvariantCulture))
            .Where(index => index >= 0 && index < ctrlNames.Length)
            .ToHashSet();
        lockedNodes.UnionWith(feModel["m_LockToGoal"].Values
            .Select(value => value.ToInt32(CultureInfo.InvariantCulture))
            .Where(index => index >= 0 && index < ctrlNames.Length));
        var worldCollisionNodes = feModel["m_WorldCollisionNodes"].Values
            .Select(value => value.ToInt32(CultureInfo.InvariantCulture))
            .Where(index => index >= 0 && index < ctrlNames.Length)
            .ToHashSet();
        var twistNodes = feModel["m_Twists"].Values
            .SelectMany(value => new[]
            {
                value["nNodeOrient"].ToInt32(CultureInfo.InvariantCulture),
                value["nNodeEnd"].ToInt32(CultureInfo.InvariantCulture),
            })
            .Where(index => index >= 0 && index < ctrlNames.Length)
            .ToHashSet();
        var rods = feModel["m_Rods"].Values
            .Select(ReadRod)
            .Where(rod => rod is not null)
            .Select(rod => rod!)
            .ToArray();
        var distinctRods = rods
            .DistinctBy(rod => (rod.Node0, rod.Node1, rod.MinDistance, rod.MaxDistance, rod.Relaxation, rod.Weight0))
            .ToArray();
        var rodMultiplicity = rods
            .GroupBy(rod => (rod.Node0, rod.Node1))
            .ToDictionary(group => group.Key, group => group.Count());
        var animatedLengthNodes = ReadAnimatedLengthNodes(feModel, ctrlNames.Length);
        var vertexMaps = ReadVertexMaps(feModel, staticNodeCount, ctrlNames.Length);
        var collisionMasks = feModel.TryGetValue("m_TreeCollisionMasks", out var treeCollisionMasks)
            ? treeCollisionMasks.Values.Select(value => value.ToInt32(CultureInfo.InvariantCulture)).ToArray()
            : Array.Empty<int>();
        var stiffHinges = ReadStiffHinges(feModel, ctrlNames.Length);
        if (animatedLengthNodes.Count > 0)
        {
            warnings.Add("cloth animated-length constraints were recovered; explicit_length is not retained separately by compiled physics and was canonicalized to 0");
        }
        if (stiffHinges.Count > 0)
        {
            warnings.Add("cloth stiff-hinge strength was recovered; authored stiff_hinge_angle is not retained directly and was canonicalized to 0");
        }

        var strayRadii = new Dictionary<int, (float Radius, float Stretchiness)>();
        if (feModel.TryGetValue("m_AnimStrayRadii", out var strayRadiusData))
        {
            foreach (var value in strayRadiusData.Values)
            {
                if (!value.IsCollection || !value.TryGetValue("nNode", out var nodeData))
                {
                    continue;
                }

                var node = nodeData.Values
                    .Select(item => item.ToInt32(CultureInfo.InvariantCulture))
                    .FirstOrDefault(index => index >= 0 && index < ctrlNames.Length, -1);
                if (node < 0)
                {
                    continue;
                }

                var radius = value.TryGetValue("flMaxDist", out var maxDistance)
                    ? maxDistance.ToSingle(CultureInfo.InvariantCulture)
                    : 0.0f;
                var relaxation = value.TryGetValue("flRelaxationFactor", out var relaxationFactor)
                    ? relaxationFactor.ToSingle(CultureInfo.InvariantCulture)
                    : 1.0f;
                strayRadii[node] = (radius, Math.Clamp(1.0f - relaxation, 0.0f, 1.0f));
            }
        }

        var extrusionChildren = new Dictionary<int, List<(int Child, float Radius, Vector3 Offset)>>();
        var endEffectors = new Dictionary<int, float>();
        foreach (var offset in feModel["m_CtrlOffsets"].Values)
        {
            var parent = offset["nCtrlParent"].ToInt32(CultureInfo.InvariantCulture);
            var child = offset["nCtrlChild"].ToInt32(CultureInfo.InvariantCulture);
            if (parent < 0 || parent >= ctrlNames.Length || child < 0 || child >= ctrlNames.Length
                || !ctrlNames[child].StartsWith("$cc", StringComparison.Ordinal))
            {
                continue;
            }

            var components = offset["vOffset"].Values
                .Select(value => value.ToSingle(CultureInfo.InvariantCulture))
                .ToArray();
            var offsetVector = components.Length >= 3
                ? new Vector3(components[0], components[1], components[2])
                : Vector3.Zero;
            var radius = offsetVector.Length();
            if (ctrlNames[child].EndsWith("_Ctr", StringComparison.Ordinal))
            {
                endEffectors[parent] = radius;
                continue;
            }

            if (!extrusionChildren.TryGetValue(parent, out var children))
            {
                children = new List<(int Child, float Radius, Vector3 Offset)>();
                extrusionChildren.Add(parent, children);
            }
            children.Add((child, radius, offsetVector));
        }

        var realNodeIndices = Enumerable.Range(0, ctrlNames.Length)
            .Where(index => !ctrlNames[index].StartsWith("$cc", StringComparison.Ordinal))
            .ToArray();
        var realNodeSet = realNodeIndices.ToHashSet();
        var roots = realNodeIndices
            .Where(index => parents[index] < 0 || !realNodeSet.Contains(parents[index]))
            .ToArray();
        var chains = new List<RetailClothChain>();

        foreach (var root in roots)
        {
            var included = new HashSet<int> { root };
            var pending = new Queue<int>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                foreach (var child in realNodeIndices.Where(index => parents[index] == parent))
                {
                    if (included.Add(child))
                    {
                        pending.Enqueue(child);
                    }
                }
            }

            var maxRodMultiplicity = rodMultiplicity
                .Where(pair => included.Contains(pair.Key.Item1) && included.Contains(pair.Key.Item2))
                .Select(pair => pair.Value)
                .DefaultIfEmpty(1)
                .Max();
            var extraIterations = Math.Max(
                0,
                (int)Math.Round(Math.Log2(maxRodMultiplicity), MidpointRounding.AwayFromZero));
            var nodes = included
                .OrderBy(index => index)
                .Select(index =>
                {
                    var dynamicIndex = index - staticNodeCount;
                    var integrator = index < integrators.Length ? integrators[index] : default;
                    var inverseMass = index < inverseMasses.Length ? inverseMasses[index] : 0.0f;
                    var extrusion = extrusionChildren.TryGetValue(index, out var children)
                        ? children
                        : null;
                    var extrusionAxis = extrusion is { Count: > 0 }
                        ? RecoverExtrudeForwardAxis(extrusion[0].Offset)
                        : null;
                    var stray = strayRadii.TryGetValue(index, out var strayRadius)
                        ? strayRadius
                        : ((float Radius, float Stretchiness)?)null;
                    var parentIndex = parents[index] >= 0 && realNodeSet.Contains(parents[index])
                        ? parents[index]
                        : -1;
                    var grandParentIndex = parentIndex >= 0
                        && parents[parentIndex] >= 0
                        && realNodeSet.Contains(parents[parentIndex])
                            ? parents[parentIndex]
                            : -1;
                    var stretchRod = FindRod(distinctRods, parentIndex, index);
                    var bendRod = FindRod(distinctRods, grandParentIndex, index);
                    var antishrink = RecoverAntishrink(stretchRod, bendRod);
                    var rootConstraintRods = distinctRods
                        .Where(rod => Connects(rod, root, index)
                            && root != parentIndex
                            && root != grandParentIndex
                            && rod.Relaxation < 0.999999f)
                        .OrderByDescending(rod => rod.Relaxation)
                        .ToArray();
                    var childrenIndices = realNodeIndices
                        .Where(candidate => parents[candidate] == index)
                        .ToArray();
                    var siblingSpring = RecoverSiblingSpring(distinctRods, childrenIndices);
                    var collisionMask = dynamicIndex >= 0 && dynamicIndex < collisionMasks.Length
                        ? collisionMasks[dynamicIndex]
                        : 15;
                    var hinge = stiffHinges.TryGetValue(index, out var stiffHinge)
                        ? stiffHinge
                        : null;
                    return new RetailClothNode(
                        ctrlNames[index],
                        parentIndex >= 0
                            ? ctrlNames[parentIndex]
                            : null,
                        index >= staticNodeCount,
                        index >= rotationLockedStaticNodeCount,
                        stretchRod?.Relaxation,
                        siblingSpring,
                        bendRod?.Relaxation,
                        rootConstraintRods.ElementAtOrDefault(0)?.Relaxation,
                        animatedLengthNodes.Contains(index) ? 0.0f : null,
                        animatedLengthNodes.Contains(index),
                        inverseMass > 0.0f ? 1.0f : null,
                        dynamicIndex >= 0 && dynamicIndex < collisionRadii.Length
                            ? collisionRadii[dynamicIndex]
                            : null,
                        dynamicIndex >= 0 && dynamicIndex < frictions.Length
                            ? frictions[dynamicIndex]
                            : null,
                        integrator is not null && integrator.IsCollection
                            ? RecoverCubicControl(integrator["flAnimationForceAttraction"].ToSingle(CultureInfo.InvariantCulture))
                            : null,
                        integrator is not null && integrator.IsCollection
                            ? RecoverGoalDamping(
                                integrator["flAnimationForceAttraction"].ToSingle(CultureInfo.InvariantCulture),
                                integrator["flAnimationVertexAttraction"].ToSingle(CultureInfo.InvariantCulture))
                            : null,
                        integrator is not null && integrator.IsCollection
                            ? RecoverCubicControl(integrator["flPointDamping"].ToSingle(CultureInfo.InvariantCulture))
                            : null,
                        integrator is not null && integrator.IsCollection
                            ? integrator["flGravity"].ToSingle(CultureInfo.InvariantCulture) / 360.0f
                            : null,
                        stray?.Radius,
                        stray?.Stretchiness,
                        rootConstraintRods.ElementAtOrDefault(1)?.Relaxation,
                        antishrink,
                        vertexMaps.GetValueOrDefault(index),
                        endEffectors.TryGetValue(index, out var endEffector) ? endEffector : null,
                        hinge?.Strength,
                        hinge?.Angle,
                        RecoverMotionBias(stretchRod, parentIndex >= staticNodeCount),
                        (collisionMask & 1) != 0,
                        (collisionMask & 2) != 0,
                        (collisionMask & 4) != 0,
                        (collisionMask & 8) != 0,
                        lockedNodes.Contains(index),
                        worldCollisionNodes.Contains(index),
                        twistNodes.Contains(index),
                        extraIterations,
                        extrusion?.Count ?? 0,
                        extrusion is { Count: > 0 } ? extrusion.Average(item => item.Radius) : null,
                        extrusion is { Count: > 0 } ? RecoverExtrudeTwist(extrusion[0].Offset, extrusionAxis!) : null,
                        extrusionAxis);
                })
                .ToArray();
            var chain = RepairMissingParentAnchors(
                new RetailClothChain(ctrlNames[root], nodes),
                out var addedAnchors);
            if (addedAnchors.Count > 0)
            {
                warnings.Add(
                    $"cloth chain '{ctrlNames[root]}' referenced parent joint(s) outside its recovered node set; " +
                    $"added fixed anchor(s): {string.Join(", ", addedAnchors)}");
            }
            chains.Add(chain);
        }

        return new RetailClothReadResult(chains, warnings);
    }

    private static RetailClothChain RepairMissingParentAnchors(
        RetailClothChain chain,
        out IReadOnlyList<string> addedAnchors)
    {
        var missingParents = FindMissingParentJointNames(
            chain.Nodes.Select(node => node.Name).ToArray(),
            chain.Nodes.Select(node => node.Parent).ToArray());
        addedAnchors = missingParents;
        if (missingParents.Length == 0)
        {
            return chain;
        }

        var anchors = missingParents
            .Select(CreateFixedClothAnchor)
            .ToArray();
        var rootBone = missingParents.Length == 1
            ? missingParents[0]
            : chain.RootBone;
        return new RetailClothChain(rootBone, anchors.Concat(chain.Nodes).ToArray());
    }

    private static string[] FindMissingParentJointNames(
        IReadOnlyCollection<string> nodeNames,
        IReadOnlyCollection<string?> parentNames)
    {
        var names = nodeNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return parentNames
            .Where(parent => !string.IsNullOrWhiteSpace(parent) && !names.Contains(parent!))
            .Select(parent => parent!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(parent => parent, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RetailClothNode CreateFixedClothAnchor(string name) => new(
        name,
        Parent: null,
        Simulate: false,
        AllowRotation: true,
        StretchSpring: null,
        ChildSiblingSpring: null,
        BendSpring: null,
        TorsionSpring: null,
        ExplicitLength: null,
        AnimatedLength: false,
        Mass: null,
        CollisionRadius: null,
        Friction: null,
        GoalStrength: null,
        GoalDamping: null,
        Drag: null,
        GravityZ: null,
        StrayRadius: null,
        StrayRadiusStretchiness: null,
        SuspenderSpring: null,
        AntishrinkStrength: null,
        VertexMap: null,
        EndEffector: null,
        StiffHinge: null,
        StiffHingeAngle: null,
        MotionBias: null,
        CollisionLayer0: true,
        CollisionLayer1: true,
        CollisionLayer2: true,
        CollisionLayer3: true,
        LockTranslation: false,
        WorldCollision: false,
        HasTwistConstraint: false,
        ExtraIterations: 0,
        ExtrudeSides: 0,
        ExtrudeRadius: null,
        ExtrudeTwist: null,
        ExtrudeForwardAxis: null);

    private static RetailRod? ReadRod(KVObject value)
    {
        if (!value.IsCollection || !value.TryGetValue("nNode", out var nodeData))
        {
            return null;
        }

        var nodes = nodeData.Values
            .Select(item => item.ToInt32(CultureInfo.InvariantCulture))
            .Take(2)
            .ToArray();
        if (nodes.Length != 2 || nodes[0] < 0 || nodes[1] < 0 || nodes[0] == nodes[1])
        {
            return null;
        }

        var node0 = Math.Min(nodes[0], nodes[1]);
        var node1 = Math.Max(nodes[0], nodes[1]);
        return new RetailRod(
            node0,
            node1,
            ReadSingle(value, "flMinDist", 0.0f),
            ReadSingle(value, "flMaxDist", 0.0f),
            ReadSingle(value, "flRelaxationFactor", 1.0f),
            ReadSingle(value, "flWeight0", 0.5f));
    }

    private static float ReadSingle(KVObject value, string name, float fallback) =>
        value.TryGetValue(name, out var field)
            ? field.ToSingle(CultureInfo.InvariantCulture)
            : fallback;

    private static bool Connects(RetailRod rod, int node0, int node1)
    {
        if (node0 < 0 || node1 < 0 || node0 == node1)
        {
            return false;
        }

        var low = Math.Min(node0, node1);
        var high = Math.Max(node0, node1);
        return rod.Node0 == low && rod.Node1 == high;
    }

    private static RetailRod? FindRod(IEnumerable<RetailRod> rods, int node0, int node1) =>
        rods.FirstOrDefault(rod => Connects(rod, node0, node1));

    private static float? RecoverAntishrink(params RetailRod?[] rods)
    {
        var ratios = rods
            .Where(rod => rod is not null && rod.MaxDistance > 1.0e-6f)
            .Select(rod => Math.Clamp(rod!.MinDistance / rod.MaxDistance, 0.0f, 1.0f))
            .ToArray();
        return ratios.Length > 0 ? ratios.Min() : null;
    }

    private static float? RecoverSiblingSpring(
        IReadOnlyList<RetailRod> rods,
        IReadOnlyList<int> children)
    {
        for (var left = 0; left < children.Count; left++)
        {
            for (var right = left + 1; right < children.Count; right++)
            {
                var rod = FindRod(rods, children[left], children[right]);
                if (rod is not null)
                {
                    return rod.Relaxation;
                }
            }
        }

        return null;
    }

    private static float? RecoverMotionBias(RetailRod? rod, bool parentIsDynamic)
    {
        if (!parentIsDynamic
            || rod is null
            || !float.IsFinite(rod.Weight0)
            || rod.Weight0 < 0.0f
            || rod.Weight0 >= 1.0f)
        {
            return null;
        }

        // ResourceCompiler stores equal-mass rod bias as w=(1-b)/(2-b).
        var bias = (1.0f - (2.0f * rod.Weight0)) / (1.0f - rod.Weight0);
        return Math.Clamp(bias, 0.0f, 1.0f);
    }

    private static HashSet<int> ReadAnimatedLengthNodes(KVObject feModel, int nodeCount)
    {
        var result = new HashSet<int>();
        if (!feModel.TryGetValue("m_SimdRodsAnim", out var animatedRods))
        {
            return result;
        }

        foreach (var rod in animatedRods.Values)
        {
            if (!rod.IsCollection || !rod.TryGetValue("nNode", out var lanes))
            {
                continue;
            }

            var laneValues = lanes.Values.ToArray();
            if (laneValues.Length < 2)
            {
                continue;
            }

            foreach (var nodeValue in laneValues[1].Values)
            {
                var node = nodeValue.ToInt32(CultureInfo.InvariantCulture);
                if (node >= 0 && node < nodeCount)
                {
                    result.Add(node);
                }
            }
        }

        return result;
    }

    private static Dictionary<int, string> ReadVertexMaps(
        KVObject feModel,
        int staticNodeCount,
        int nodeCount)
    {
        var result = new Dictionary<int, string>();
        if (!feModel.TryGetValue("m_VertexMaps", out var vertexMaps))
        {
            return result;
        }

        foreach (var map in vertexMaps.Values)
        {
            if (!map.IsCollection || !map.TryGetValue("sName", out var nameData))
            {
                continue;
            }

            var name = nameData.ToString();
            var vertexBase = map.TryGetValue("nVertexBase", out var baseData)
                ? baseData.ToInt32(CultureInfo.InvariantCulture)
                : -1;
            var vertexCount = map.TryGetValue("nVertexCount", out var countData)
                ? countData.ToInt32(CultureInfo.InvariantCulture)
                : 0;
            for (var offset = 0; offset < vertexCount; offset++)
            {
                var node = staticNodeCount + vertexBase + offset;
                if (node >= 0 && node < nodeCount && !string.IsNullOrWhiteSpace(name))
                {
                    result[node] = name;
                }
            }
        }

        return result;
    }

    private static Dictionary<int, RetailStiffHinge> ReadStiffHinges(KVObject feModel, int nodeCount)
    {
        var result = new Dictionary<int, RetailStiffHinge>();
        if (!feModel.TryGetValue("m_KelagerBends", out var hinges)
            || !feModel.TryGetValue("m_InitPose", out var initPose))
        {
            return result;
        }

        var positions = initPose.Values.Select(ReadPosition).ToArray();
        foreach (var hinge in hinges.Values)
        {
            if (!hinge.IsCollection
                || !hinge.TryGetValue("nNode", out var nodeData)
                || !hinge.TryGetValue("flWeight", out var weightData))
            {
                continue;
            }

            var nodes = nodeData.Values
                .Select(value => value.ToInt32(CultureInfo.InvariantCulture))
                .Take(3)
                .ToArray();
            var weights = weightData.Values
                .Select(value => value.ToSingle(CultureInfo.InvariantCulture))
                .Take(3)
                .ToArray();
            if (nodes.Length != 3 || weights.Length != 3
                || nodes.Any(node => node < 0 || node >= nodeCount || node >= positions.Length))
            {
                continue;
            }

            var first = positions[nodes[1]] - positions[nodes[0]];
            var second = positions[nodes[2]] - positions[nodes[0]];
            var denominator = first.Length() * second.Length();
            var geometryFactor = denominator > 1.0e-6f
                ? Math.Abs(Vector3.Dot(first, second) / denominator)
                : 0.0f;
            var strength = geometryFactor > 1.0e-6f
                ? Math.Clamp(Math.Abs(weights[0]) / geometryFactor, 0.0f, 1.0f)
                : Math.Clamp(Math.Abs(weights[0]), 0.0f, 1.0f);
            result[nodes[1]] = new RetailStiffHinge(strength, 0.0f);
        }

        return result;
    }

    private static Vector3 ReadPosition(KVObject value)
    {
        var components = value.Values
            .Select(item => item.ToSingle(CultureInfo.InvariantCulture))
            .Take(3)
            .ToArray();
        return components.Length == 3
            ? new Vector3(components[0], components[1], components[2])
            : Vector3.Zero;
    }

    private static IReadOnlyList<string> FindLossyClothFeatures(KVObject feModel)
    {
        var warnings = new List<string>();
        AddWarningForNonEmptyField(feModel, warnings, "m_SpringIntegrator",
            "cloth spring settings are present in retail physics and cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_SimdSpringIntegrator",
            "cloth spring settings are present in retail physics and cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_AxialEdges",
            "cloth length/axial constraints are present in retail physics and cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_HingeLimits",
            "cloth hinge limits are present in retail physics and cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_FollowNodes",
            "cloth follow/end-effector settings are present in retail physics and cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_VertexMapValues",
            "cloth vertex-map values are present in retail physics; map names and node ranges were recovered but per-vertex values cannot be reconstructed exactly");
        AddWarningForNonEmptyField(feModel, warnings, "m_Effects",
            "cloth effects are present in retail physics and cannot be reconstructed exactly");
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddWarningForNonEmptyField(
        KVObject feModel,
        ICollection<string> warnings,
        string fieldName,
        string warning)
    {
        if (feModel.TryGetValue(fieldName, out var value) && value.Count > 0)
        {
            warnings.Add(warning);
        }
    }

    private static float RecoverCubicControl(float compiledValue) =>
        compiledValue <= 0.0f
            ? 0.0f
            : (float)Math.Cbrt(Math.Clamp(compiledValue, 0.0f, 1.0f));

    private static float RecoverGoalDamping(float forceAttraction, float vertexAttraction)
    {
        forceAttraction = Math.Clamp(forceAttraction, 0.0f, 1.0f);
        vertexAttraction = Math.Clamp(vertexAttraction, forceAttraction, 1.0f);
        if (vertexAttraction <= forceAttraction + 1.0e-7f || forceAttraction >= 1.0f)
        {
            return 0.0f;
        }

        var strength = RecoverCubicControl(forceAttraction);
        var logarithmicResponse = -Math.Log(
            Math.Max(1.0e-12, (1.0 - vertexAttraction) / (1.0 - forceAttraction)));
        var responsePerUnit = InterpolateGoalDampingResponse(strength);
        var recovered = responsePerUnit > 0.0
            ? logarithmicResponse / responsePerUnit
            : 0.0;

        // ModelDoc cloth damping is authored in thousandths. Snapping removes the
        // floating-point integration noise stored in the compiled FE model.
        return (float)Math.Clamp(Math.Round(recovered, 3, MidpointRounding.AwayFromZero), 0.0, 1.0);
    }

    private static double InterpolateGoalDampingResponse(float strength)
    {
        // Calibrated against ResourceCompiler's small-damping response. These are
        // control-space strengths, before the compiler's cubic strength curve.
        ReadOnlySpan<(double Strength, double Response)> points =
        [
            (0.20, 22.4525),
            (0.30, 12.3380),
            (0.35, 9.8720),
            (0.40, 8.0395),
            (0.45, 7.1000),
            (0.50, 6.0465),
            (0.80, 4.0000),
            (0.95, 5.7185),
        ];

        if (strength <= points[0].Strength)
        {
            return points[0].Response;
        }

        for (var index = 1; index < points.Length; index++)
        {
            if (strength <= points[index].Strength)
            {
                var left = points[index - 1];
                var right = points[index];
                var alpha = (strength - left.Strength) / (right.Strength - left.Strength);
                return left.Response + ((right.Response - left.Response) * alpha);
            }
        }

        return points[^1].Response;
    }

    private static string RecoverExtrudeForwardAxis(Vector3 offset) =>
        Math.Abs(offset.X) > Math.Max(Math.Abs(offset.Y), Math.Abs(offset.Z))
            ? "Z"
            : "X";

    private static float RecoverExtrudeTwist(Vector3 offset, string forwardAxis)
    {
        if (offset.LengthSquared() <= 1.0e-12f)
        {
            return 0.0f;
        }

        // Compiled $cc offsets retain the extrusion plane and its authored twist.
        // X is the canonical axis for the equivalent X/Y two-sided representation.
        var degrees = string.Equals(forwardAxis, "Z", StringComparison.Ordinal)
            ? Math.Atan2(-offset.X, offset.Y) * (180.0 / Math.PI)
            : Math.Atan2(offset.Z, offset.Y) * (180.0 / Math.PI);
        return (float)Math.Round(degrees, 3, MidpointRounding.AwayFromZero);
    }

    private static RetailPhysicsJoint ReadPhysicsJoint(KVObject value, IReadOnlyList<string> boneNames)
    {
        var type = value["m_nType"].ToInt32(CultureInfo.InvariantCulture);
        if (type is not (3 or 4))
        {
            throw new InvalidDataException(
                $"Retail ragdoll contains unsupported physics joint type {type}; refusing a lossy conversion.");
        }

        var body1 = value["m_nBody1"].ToInt32(CultureInfo.InvariantCulture);
        var body2 = value["m_nBody2"].ToInt32(CultureInfo.InvariantCulture);
        if (body1 < 0 || body1 >= boneNames.Count || body2 < 0 || body2 >= boneNames.Count)
        {
            throw new InvalidDataException(
                $"Retail ragdoll joint references invalid body indices {body1} and {body2}.");
        }

        var frame = value["m_Frame1"].Values.ToArray();
        if (frame.Length < 8)
        {
            throw new InvalidDataException("Retail ragdoll joint has a malformed parent frame.");
        }

        var position = frame.Take(3)
            .Select(item => item.ToSingle(CultureInfo.InvariantCulture))
            .ToArray();
        var rotation = frame.Skip(4).Take(4)
            .Select(item => item.ToSingle(CultureInfo.InvariantCulture))
            .ToArray();
        var quaternion = Quaternion.Normalize(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]));
        var angles = QuaternionToSourceAngles(quaternion);

        var swingLimit = value["m_SwingLimit"];
        var twistLimit = value["m_TwistLimit"];
        return new RetailPhysicsJoint(
            type,
            boneNames[body1],
            boneNames[body2],
            new Vector3(position[0], position[1], position[2]),
            angles,
            value["m_bEnableCollision"].ToBoolean(CultureInfo.InvariantCulture),
            value["m_flFriction"].ToSingle(CultureInfo.InvariantCulture),
            value["m_bEnableSwingLimit"].ToBoolean(CultureInfo.InvariantCulture),
            RadiansToDegrees(swingLimit["m_flMax"].ToSingle(CultureInfo.InvariantCulture)),
            value["m_bEnableTwistLimit"].ToBoolean(CultureInfo.InvariantCulture),
            RadiansToDegrees(twistLimit["m_flMin"].ToSingle(CultureInfo.InvariantCulture)),
            RadiansToDegrees(twistLimit["m_flMax"].ToSingle(CultureInfo.InvariantCulture)));
    }

    private static Vector3 QuaternionToSourceAngles(Quaternion value)
    {
        var sinRollCosPitch = 2.0 * ((value.W * value.X) + (value.Y * value.Z));
        var cosRollCosPitch = 1.0 - (2.0 * ((value.X * value.X) + (value.Y * value.Y)));
        var roll = Math.Atan2(sinRollCosPitch, cosRollCosPitch);

        var sinPitch = 2.0 * ((value.W * value.Y) - (value.Z * value.X));
        var pitch = Math.Abs(sinPitch) >= 1.0
            ? Math.CopySign(Math.PI / 2.0, sinPitch)
            : Math.Asin(sinPitch);

        var sinYawCosPitch = 2.0 * ((value.W * value.Z) + (value.X * value.Y));
        var cosYawCosPitch = 1.0 - (2.0 * ((value.Y * value.Y) + (value.Z * value.Z)));
        var yaw = Math.Atan2(sinYawCosPitch, cosYawCosPitch);

        return new Vector3(
            (float)RadiansToDegrees((float)pitch),
            (float)RadiansToDegrees((float)yaw),
            (float)RadiansToDegrees((float)roll));
    }

    private static string CreatePhysicsJointList(IReadOnlyList<RetailPhysicsJoint> joints)
    {
        var text = new StringBuilder();
        text.AppendLine("{");
        text.AppendLine("\t_class = \"PhysicsJointList\"");
        text.AppendLine("\tchildren =");
        text.AppendLine("\t[");
        foreach (var joint in joints)
        {
            text.AppendLine("\t\t{");
            text.AppendLine(joint.Type == 3
                ? "\t\t\t_class = \"PhysicsJointRevolute\""
                : "\t\t\t_class = \"PhysicsJointConical\"");
            text.AppendLine($"\t\t\tparent_body = \"{EscapeKv3(joint.ParentBody)}\"");
            text.AppendLine($"\t\t\tchild_body = \"{EscapeKv3(joint.ChildBody)}\"");
            text.AppendLine($"\t\t\tanchor_origin = {FormatVector(joint.AnchorOrigin)}");
            text.AppendLine($"\t\t\tanchor_angles = {FormatVector(joint.AnchorAngles)}");
            text.AppendLine($"\t\t\tcollision_enabled = {FormatBoolean(joint.CollisionEnabled)}");
            text.AppendLine($"\t\t\tfriction = {FormatFloat(joint.Friction)}");
            if (joint.Type == 3)
            {
                text.AppendLine($"\t\t\tenable_limit = {FormatBoolean(joint.EnableTwistLimit)}");
                text.AppendLine($"\t\t\tmin_angle = {FormatFloat(joint.MinTwistAngle)}");
                text.AppendLine($"\t\t\tmax_angle = {FormatFloat(joint.MaxTwistAngle)}");
            }
            else
            {
                text.AppendLine($"\t\t\tenable_swing_limit = {FormatBoolean(joint.EnableSwingLimit)}");
                text.AppendLine($"\t\t\tswing_limit = {FormatFloat(joint.SwingLimit)}");
                text.AppendLine("\t\t\tswing_offset_angle = [ 0.0, 0.0, 0.0 ]");
                text.AppendLine($"\t\t\tenable_twist_limit = {FormatBoolean(joint.EnableTwistLimit)}");
                text.AppendLine($"\t\t\tmin_twist_angle = {FormatFloat(joint.MinTwistAngle)}");
                text.AppendLine($"\t\t\tmax_twist_angle = {FormatFloat(joint.MaxTwistAngle)}");
            }
            text.AppendLine("\t\t},");
        }
        text.AppendLine("\t]");
        text.Append('}');
        return text.ToString();
    }

    private static string CreateSoftbody(IReadOnlyList<RetailClothChain> chains)
    {
        var text = new StringBuilder();
        text.AppendLine("{");
        text.AppendLine("\t_class = \"Softbody\"");
        text.AppendLine("\tchildren =");
        text.AppendLine("\t[");
        foreach (var chain in chains)
        {
            text.AppendLine("\t\t{");
            text.AppendLine("\t\t\t_class = \"ClothChain\"");
            text.AppendLine($"\t\t\troot_bone = \"{EscapeKv3(chain.RootBone)}\"");
            text.AppendLine("\t\t\tchain =");
            text.AppendLine("\t\t\t{");
            text.AppendLine("\t\t\t\tjoints =");
            text.AppendLine("\t\t\t\t[");
            foreach (var node in chain.Nodes)
            {
                text.AppendLine("\t\t\t\t\t{");
                text.AppendLine($"\t\t\t\t\t\tjoint_name = \"{EscapeKv3(node.Name)}\"");
                if (node.Parent is not null)
                {
                    text.AppendLine($"\t\t\t\t\t\tjoint_parent = \"{EscapeKv3(node.Parent)}\"");
                }
                text.AppendLine($"\t\t\t\t\t\tsimulate = {FormatBoolean(node.Simulate)}");
                text.AppendLine($"\t\t\t\t\t\tallow_rotation = {FormatBoolean(node.AllowRotation)}");
                AppendOptionalFloat(text, "stretch_spring", node.StretchSpring);
                AppendOptionalFloat(text, "child_sibling_spring", node.ChildSiblingSpring);
                AppendOptionalFloat(text, "bend_spring", node.BendSpring);
                AppendOptionalFloat(text, "torsion_spring", node.TorsionSpring);
                AppendOptionalFloat(text, "explicit_length", node.ExplicitLength);
                text.AppendLine($"\t\t\t\t\t\tanimated_length = {FormatBoolean(node.AnimatedLength)}");
                AppendOptionalFloat(text, "mass", node.Mass);
                AppendOptionalFloat(text, "collision_radius", node.CollisionRadius);
                AppendOptionalFloat(text, "friction", node.Friction);
                AppendOptionalFloat(text, "goal_strength", node.GoalStrength);
                AppendOptionalFloat(text, "goal_damping", node.GoalDamping);
                AppendOptionalFloat(text, "drag", node.Drag);
                AppendOptionalFloat(text, "gravity_z", node.GravityZ);
                AppendOptionalFloat(text, "stray_radius", node.StrayRadius);
                AppendOptionalFloat(text, "stray_radius_stretchiness", node.StrayRadiusStretchiness);
                AppendOptionalFloat(text, "suspender", node.SuspenderSpring);
                AppendOptionalFloat(text, "antishrink", node.AntishrinkStrength);
                if (!string.IsNullOrWhiteSpace(node.VertexMap))
                {
                    text.AppendLine($"\t\t\t\t\t\tvertex_map = \"{EscapeKv3(node.VertexMap)}\"");
                }
                AppendOptionalFloat(text, "end_effector", node.EndEffector);
                AppendOptionalFloat(text, "stiff_hinge", node.StiffHinge);
                AppendOptionalFloat(text, "stiff_hinge_angle", node.StiffHingeAngle);
                AppendOptionalFloat(text, "motion_bias", node.MotionBias);
                text.AppendLine($"\t\t\t\t\t\tcollision_layer_0 = {FormatBoolean(node.CollisionLayer0)}");
                text.AppendLine($"\t\t\t\t\t\tcollision_layer_1 = {FormatBoolean(node.CollisionLayer1)}");
                text.AppendLine($"\t\t\t\t\t\tcollision_layer_2 = {FormatBoolean(node.CollisionLayer2)}");
                text.AppendLine($"\t\t\t\t\t\tcollision_layer_3 = {FormatBoolean(node.CollisionLayer3)}");
                if (node.LockTranslation)
                {
                    text.AppendLine("\t\t\t\t\t\tlock_translation = true");
                }
                if (node.WorldCollision)
                {
                    text.AppendLine("\t\t\t\t\t\tworld_collision = true");
                }
                if (node.HasTwistConstraint)
                {
                    text.AppendLine("\t\t\t\t\t\ttwist_relax = 1.0");
                }
                if (node.ExtraIterations > 0)
                {
                    text.AppendLine($"\t\t\t\t\t\textra_iterations = {node.ExtraIterations}");
                }
                if (node.ExtrudeSides > 0)
                {
                    text.AppendLine($"\t\t\t\t\t\textrude_sides = {node.ExtrudeSides}");
                    AppendOptionalFloat(text, "extrude_radius", node.ExtrudeRadius);
                    AppendOptionalFloat(text, "extrude_twist", node.ExtrudeTwist);
                    if (!string.IsNullOrWhiteSpace(node.ExtrudeForwardAxis))
                    {
                        text.AppendLine($"\t\t\t\t\t\textrude_forward_axis = \"{EscapeKv3(node.ExtrudeForwardAxis)}\"");
                    }
                }
                text.AppendLine("\t\t\t\t\t},");
            }
            text.AppendLine("\t\t\t\t]");
            text.AppendLine("\t\t\t\tversion = 2");
            text.AppendLine("\t\t\t}");
            text.AppendLine("\t\t},");
        }
        text.AppendLine("\t]");
        text.AppendLine("\tstiffness_on_ragdoll = 0.0");
        text.AppendLine("\tmotion_smooth_cdt = 0.0");
        text.AppendLine("\tcloth_sleep_enabled = false");
        text.AppendLine("\tcloth_immovable_hint = false");
        text.AppendLine("\tcloth_per_bone_scale_enabled = false");
        text.AppendLine("\tcloth_enable_empty_model = false");
        text.AppendLine("\tcloth_keychain_motion = false");
        text.Append('}');
        return text.ToString();
    }

    private static void AppendOptionalFloat(StringBuilder text, string name, float? value)
    {
        if (value.HasValue && float.IsFinite(value.Value))
        {
            text.AppendLine($"\t\t\t\t\t\t{name} = {FormatFloat(value.Value)}");
        }
    }

    private static string FormatVector(Vector3 value) =>
        $"[ {FormatFloat(value.X)}, {FormatFloat(value.Y)}, {FormatFloat(value.Z)} ]";

    private static string FormatFloat(double value) =>
        EnsureFloatingPointLiteral(value.ToString("0.######", CultureInfo.InvariantCulture));

    private static string EnsureFloatingPointLiteral(string value) =>
        value.Contains('.', StringComparison.Ordinal) ? value : value + ".0";

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static double RadiansToDegrees(float value) => value * (180.0 / Math.PI);

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string EscapeKv3(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record RetailPhysicsJoint(
        int Type,
        string ParentBody,
        string ChildBody,
        Vector3 AnchorOrigin,
        Vector3 AnchorAngles,
        bool CollisionEnabled,
        float Friction,
        bool EnableSwingLimit,
        double SwingLimit,
        bool EnableTwistLimit,
        double MinTwistAngle,
        double MaxTwistAngle);

    private sealed record RetailClothChain(string RootBone, IReadOnlyList<RetailClothNode> Nodes);

    private sealed record RetailRod(
        int Node0,
        int Node1,
        float MinDistance,
        float MaxDistance,
        float Relaxation,
        float Weight0);

    private sealed record RetailStiffHinge(float Strength, float Angle);

    private sealed record RetailClothReadResult(
        IReadOnlyList<RetailClothChain> Chains,
        IReadOnlyList<string> Warnings)
    {
        public static RetailClothReadResult Empty { get; } = new(
            Array.Empty<RetailClothChain>(),
            Array.Empty<string>());
    }

    private sealed record RetailClothNode(
        string Name,
        string? Parent,
        bool Simulate,
        bool AllowRotation,
        float? StretchSpring,
        float? ChildSiblingSpring,
        float? BendSpring,
        float? TorsionSpring,
        float? ExplicitLength,
        bool AnimatedLength,
        float? Mass,
        float? CollisionRadius,
        float? Friction,
        float? GoalStrength,
        float? GoalDamping,
        float? Drag,
        float? GravityZ,
        float? StrayRadius,
        float? StrayRadiusStretchiness,
        float? SuspenderSpring,
        float? AntishrinkStrength,
        string? VertexMap,
        float? EndEffector,
        float? StiffHinge,
        float? StiffHingeAngle,
        float? MotionBias,
        bool CollisionLayer0,
        bool CollisionLayer1,
        bool CollisionLayer2,
        bool CollisionLayer3,
        bool LockTranslation,
        bool WorldCollision,
        bool HasTwistConstraint,
        int ExtraIterations,
        int ExtrudeSides,
        float? ExtrudeRadius,
        float? ExtrudeTwist,
        string? ExtrudeForwardAxis);
}
