using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using ValveKeyValue;

namespace Deadlimit.Core;

internal static class CompiledModelPhysicsInheritanceSmoke
{
    public static int Run()
    {
        var merged = CompiledModelPhysicsInheritance.MergeFeModelsForSmoke(
            CreateRetailFe(),
            CreateCustomFe());
        var names = merged["m_CtrlName"].Values.Select(value => (string)value).ToArray();
        if (merged["m_nNodeCount"].ToInt32(CultureInfo.InvariantCulture) != 5
            || merged["m_nFirstPositionDrivenNode"].ToInt32(CultureInfo.InvariantCulture) != 4
            || !names.SequenceEqual(["retail_static", "retail_a", "retail_b", "custom_ear", "retail_driven"]))
        {
            return 1;
        }

        var remappedNode = merged["m_NodeBases"].Values.Single()["nNode"]
            .ToInt32(CultureInfo.InvariantCulture);
        var customJiggleNode = merged["m_JiggleBones"].Values.Single()["m_nNode"]
            .ToInt32(CultureInfo.InvariantCulture);
        if (remappedNode != 4 || customJiggleNode != 3)
        {
            return 2;
        }
        if (merged["m_TreeParents"].Count != 7
            || merged["m_TreeChildren"].Count != 3
            || merged["m_DynNodeWindBases"].Count != 4)
        {
            return 3;
        }

        var resource = CreateCompiledResource();
        var replacement = Encoding.ASCII.GetBytes("replacement-physics-block-that-is-longer");
        var rewritten = CompiledResourceBlockRewriter.Replace(resource, "PHYS", replacement);
        if (BinaryPrimitives.ReadUInt32LittleEndian(rewritten.AsSpan(0, 4)) != rewritten.Length
            || !ReadBlock(rewritten, "PHYS").SequenceEqual(replacement)
            || !ReadBlock(rewritten, "DATA").SequenceEqual(Encoding.ASCII.GetBytes("unchanged-data")))
        {
            return 4;
        }
        return 0;
    }

    private static KVObject CreateRetailFe()
    {
        var fe = KVObject.Collection();
        fe["m_CtrlHash"] = Array(1u, 2u, 3u, 4u);
        fe["m_CtrlName"] = Array("retail_static", "retail_a", "retail_b", "retail_driven");
        fe["m_nNodeCount"] = new KVObject(4u);
        fe["m_nStaticNodes"] = new KVObject(1u);
        fe["m_nFirstPositionDrivenNode"] = new KVObject(3u);
        fe["m_nDynamicNodeFlags"] = new KVObject(1024u);
        fe["m_nTreeDepth"] = new KVObject((byte)2);
        fe["m_InitPose"] = ArrayOfArrays(4, 8);
        fe["m_NodeInvMasses"] = Array(0f, 1f, 1f, 0f);
        fe["m_NodeIntegrator"] = Collections(4);
        fe["m_SkelParents"] = Array(-1, -1, -1, 3);
        fe["m_DynNodeWindBases"] = Collections(3);
        fe["m_NodeCollisionRadii"] = Array(1f, 1f, 1f);
        fe["m_DynNodeFriction"] = Array(0f, 0f, 0f);
        fe["m_SourceElems"] = Array(0u, 0u, 0u, 0u, 0u);
        fe["m_JiggleBones"] = KVObject.Array(0);
        fe["m_TreeParents"] = Array((ushort)3, (ushort)3, (ushort)4, (ushort)4, ushort.MaxValue);
        fe["m_TreeCollisionMasks"] = Array((ushort)1, (ushort)1, (ushort)1, (ushort)1, (ushort)1);
        fe["m_TreeChildren"] = KVObject.Array(
        [
            TreeChild(0, 1),
            TreeChild(3, 2),
        ]);
        var nodeBase = KVObject.Collection();
        nodeBase["nNode"] = new KVObject(3u);
        fe["m_NodeBases"] = KVObject.Array([nodeBase]);
        return fe;
    }

    private static KVObject CreateCustomFe()
    {
        var fe = KVObject.Collection();
        fe["m_CtrlHash"] = Array(99u);
        fe["m_CtrlName"] = Array("custom_ear");
        fe["m_nNodeCount"] = new KVObject(1u);
        fe["m_nStaticNodes"] = new KVObject(0u);
        fe["m_nFirstPositionDrivenNode"] = new KVObject(1u);
        fe["m_nDynamicNodeFlags"] = new KVObject(8192u);
        fe["m_nTreeDepth"] = new KVObject((byte)0);
        fe["m_InitPose"] = ArrayOfArrays(1, 8);
        fe["m_NodeInvMasses"] = Array(1f);
        fe["m_NodeIntegrator"] = Collections(1);
        fe["m_SkelParents"] = Array(-1);
        fe["m_DynNodeWindBases"] = Collections(1);
        fe["m_NodeCollisionRadii"] = KVObject.Array(0);
        fe["m_DynNodeFriction"] = KVObject.Array(0);
        fe["m_SourceElems"] = Array(0u, 0u);
        var jiggle = KVObject.Collection();
        jiggle["m_nNode"] = new KVObject(0u);
        jiggle["m_nJiggleParent"] = new KVObject(uint.MaxValue);
        jiggle["m_jiggleBone"] = KVObject.Collection();
        fe["m_JiggleBones"] = KVObject.Array([jiggle]);
        fe["m_TreeParents"] = Array(ushort.MaxValue);
        fe["m_TreeCollisionMasks"] = Array((ushort)0);
        fe["m_TreeChildren"] = KVObject.Array(0);
        return fe;
    }

    private static byte[] CreateCompiledResource()
    {
        var physics = Encoding.ASCII.GetBytes("old-phys");
        var data = Encoding.ASCII.GetBytes("unchanged-data");
        const int tableOffset = 16;
        const int physicsOffset = 48;
        const int dataOffset = 64;
        var bytes = new byte[dataOffset + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 2);
        WriteEntry(bytes, tableOffset, "PHYS", physicsOffset, physics.Length);
        WriteEntry(bytes, tableOffset + 12, "DATA", dataOffset, data.Length);
        physics.CopyTo(bytes, physicsOffset);
        data.CopyTo(bytes, dataOffset);
        return bytes;
    }

    private static void WriteEntry(byte[] bytes, int entry, string type, int offset, int size)
    {
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, entry);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4, 4), (uint)(offset - (entry + 4)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8, 4), (uint)size);
    }

    private static byte[] ReadBlock(byte[] bytes, string type)
    {
        var table = 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        for (var index = 0; index < count; index++)
        {
            var entry = table + (index * 12);
            if (Encoding.ASCII.GetString(bytes, entry, 4) != type)
            {
                continue;
            }
            var offset = entry + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8, 4));
            return bytes.AsSpan(offset, size).ToArray();
        }
        return [];
    }

    private static KVObject TreeChild(ushort left, ushort right)
    {
        var value = KVObject.Collection();
        value["nChild"] = Array(left, right);
        return value;
    }

    private static KVObject Collections(int count) =>
        KVObject.Array(Enumerable.Range(0, count).Select(_ => KVObject.Collection()));

    private static KVObject ArrayOfArrays(int count, int width) =>
        KVObject.Array(Enumerable.Range(0, count).Select(_ =>
            KVObject.Array(Enumerable.Repeat(0f, width).Select(value => new KVObject(value)))));

    private static KVObject Array(params object[] values) => KVObject.Array(values.Select(value => value switch
    {
        string item => new KVObject(item),
        int item => new KVObject(item),
        uint item => new KVObject(item),
        ushort item => new KVObject(item),
        byte item => new KVObject((ushort)item),
        float item => new KVObject(item),
        _ => throw new InvalidOperationException($"Unsupported smoke value {value.GetType().Name}."),
    }));
}
