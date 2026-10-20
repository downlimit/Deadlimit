using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal static class CompiledModelSoundRepairSmoke
{
    private const string PathName = "models/heroes_wip/sound_smoke/sound_smoke.vmdl_c";

    public static int Run()
    {
        var retail = Fixture(current: true);
        var old = Fixture(current: false, custom: true);
        var repaired = CompiledModelSoundRepair.Repair(old, retail, PathName);
        Require(repaired.Modified && repaired.Bytes.Length < old.Length + 128, "Legacy sound schema migrated without payload growth");
        using (var before = CompiledModelIntegrity.Read(old, PathName))
        using (var after = CompiledModelIntegrity.Read(repaired.Bytes, PathName))
        {
            var original = ((Model)before.DataBlock!).KeyValues[CompiledModelSoundRepair.LegacySection];
            var metadata = ((Model)after.DataBlock!).KeyValues;
            Require(!metadata.ContainsKey(CompiledModelSoundRepair.LegacySection)
                && CompiledModelIntegrity.Fingerprint(original)
                == CompiledModelIntegrity.Fingerprint(metadata[CompiledModelSoundRepair.CurrentSection]),
                "Custom sound-table references, flags and settings preserved");
        }
        var again = CompiledModelSoundRepair.Repair(repaired.Bytes, retail, PathName);
        Require(!again.Modified && ReferenceEquals(again.Bytes, repaired.Bytes), "Repeated sound repair is byte-identical");
        Require(CompiledModelSoundRepair.Repair(Fixture(current: true, custom: true), retail, PathName).Status
            == SoundSettingsRepairStatus.Current, "Current author sound settings preserved");
        Require(CompiledModelSoundRepair.Repair(old, Fixture(current: false), PathName).Status
            == SoundSettingsRepairStatus.NotApplicable, "Older game schema is not guessed");
        Require(CompiledModelSoundRepair.Repair(Fixture(current: false, missing: true), retail, PathName).Status
            == SoundSettingsRepairStatus.NotApplicable, "Missing author sound settings are not invented");
        Require(CompiledModelSoundRepair.Repair(Fixture(current: false, noMetadata: true), retail, PathName).Status
            == SoundSettingsRepairStatus.NotApplicable, "Model without a metadata container is a safe no-op");
        Require(CompiledModelSoundRepair.Repair(old, Fixture(current: true, noMetadata: true), PathName).Status
            == SoundSettingsRepairStatus.NotApplicable, "Retail model without metadata is a safe no-op");
        var conflict = Fixture(current: false, conflicting: true);
        var skipped = CompiledModelSoundRepair.Repair(conflict, retail, PathName);
        Require(skipped.Status == SoundSettingsRepairStatus.ReviewNeeded && ReferenceEquals(conflict, skipped.Bytes),
            "Conflicting author sound settings are preserved");
        var malformed = Fixture(current: false, malformed: true);
        Require(CompiledModelSoundRepair.Repair(malformed, retail, PathName).Status == SoundSettingsRepairStatus.ReviewNeeded,
            "Unrecognized legacy data is preserved");
        return 0;
    }

    private static byte[] Fixture(bool current, bool custom = false, bool missing = false,
        bool conflicting = false, bool malformed = false, bool noMetadata = false)
    {
        var metadata = KVObject.Collection();
        metadata["artist_metadata"] = new KVObject("retain geometry, materials, skeleton, physics and animation");
        var settings = KVObject.Collection();
        settings["m_taggedSounds"] = KVObject.Array(custom
            ? [Sound("scripts/tagged_sounds/hero.vdata"), Sound("scripts/tagged_sounds/artist.vdata")]
            : [Sound("scripts/tagged_sounds/hero.vdata")]);
        settings["artist_marker"] = new KVObject(custom ? 42 : 1);
        if (!missing) metadata[current ? CompiledModelSoundRepair.CurrentSection : CompiledModelSoundRepair.LegacySection]
            = malformed ? new KVObject("unexpected author value") : settings;
        if (conflicting) metadata[CompiledModelSoundRepair.CurrentSection] = KVObject.Collection();
        var data = KVObject.Collection();
        var info = KVObject.Collection();
        info["m_keyValueText"] = new KVObject(metadata.ToKV3String());
        if (!noMetadata) data["m_modelInfo"] = info;
        data["artist_model_marker"] = new KVObject("unchanged");
        using var resource = new Resource(ResourceType.Model);
        resource.Blocks.Add(new WritableKv3(data, BlockType.DATA) { Resource = resource });
        var mesh = KVObject.Collection();
        mesh["geometry_marker"] = KVObject.Array([new KVObject(1f), new KVObject(2f), new KVObject(3f)]);
        resource.Blocks.Add(new WritableKv3(mesh, BlockType.MDAT) { Resource = resource });
        using var output = new MemoryStream();
        resource.Serialize(output);
        return output.ToArray();
    }

    private static KVObject Sound(string path) => new(path) { Flag = KVFlag.Resource };
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException("Tagged-sound smoke failed: " + message);
    }
    private sealed class WritableKv3 : BinaryKV3
    {
        public WritableKv3(KVObject data, BlockType type) : base(type) { Data = data.ToKV3Document(); }
    }
}
