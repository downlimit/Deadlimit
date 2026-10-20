using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal enum SoundSettingsRepairStatus { Current, Repaired, NotApplicable, ReviewNeeded }

internal sealed record SoundSettingsRepairResult(byte[] Bytes, SoundSettingsRepairStatus Status, string Detail)
{
    public bool Modified => Status == SoundSettingsRepairStatus.Repaired;
}

internal static class CompiledModelSoundRepair
{
    internal const string LegacySection = "CitadelTaggedSoundSettings_t";
    internal const string CurrentSection = "TaggedSoundSettings_t";

    public static SoundSettingsRepairResult Repair(byte[] bytes, byte[] retailBytes, string path)
    {
        using var source = CompiledModelIntegrity.Read(bytes, path);
        using var retail = CompiledModelIntegrity.Read(retailBytes, path);
        var model = (Model)source.DataBlock!;
        var original = (Model)retail.DataBlock!;
        SoundSettingsRepairResult Skip(SoundSettingsRepairStatus status, string detail) => new(bytes, status, detail);
        if (!HasMetadata(model) || !HasMetadata(original))
            return Skip(SoundSettingsRepairStatus.NotApplicable, "Model has no tagged-sound metadata container.");
        if (!original.KeyValues.ContainsKey(CurrentSection))
            return Skip(SoundSettingsRepairStatus.NotApplicable, "Retail model does not use the renamed tagged-sound schema.");
        if (!model.KeyValues.TryGetValue(LegacySection, out var legacy))
            return Skip(model.KeyValues.ContainsKey(CurrentSection) ? SoundSettingsRepairStatus.Current
                : SoundSettingsRepairStatus.NotApplicable, "No legacy tagged-sound section to migrate.");
        if (!legacy.IsCollection || !legacy.TryGetValue("m_taggedSounds", out var sounds) || !sounds.IsArray)
            return Skip(SoundSettingsRepairStatus.ReviewNeeded, "Unrecognized legacy tagged-sound data was preserved.");
        var expected = CompiledModelIntegrity.Fingerprint(legacy);
        if (model.KeyValues.TryGetValue(CurrentSection, out var current)
            && CompiledModelIntegrity.Fingerprint(current) != expected)
            return Skip(SoundSettingsRepairStatus.ReviewNeeded, "Conflicting old and current sound settings were preserved.");
        if (!model.Data.TryGetValue("m_modelInfo", out var info))
            return Skip(SoundSettingsRepairStatus.ReviewNeeded, "Model has no metadata container for tagged sounds.");

        // Rename only the confirmed schema key. Keep every authored sound-table
        // reference and resource flag; current stock sound tables stay in the game.
        var preservedModel = CompiledModelIntegrity.Fingerprint(model.Data, OmitMetadataText);
        var preservedMetadata = CompiledModelIntegrity.Fingerprint(model.KeyValues, OmitSoundSections);
        var metadata = KVObject.Collection(model.KeyValues.Children.Where(pair => !OmitSoundSections(pair.Key)));
        metadata[CurrentSection] = legacy;
        info["m_keyValueText"] = new KVObject(metadata.ToKV3String());
        var edited = new HashSet<int> { source.Blocks.IndexOf(source.DataBlock!) };
        var result = CompiledModelIntegrity.Serialize(source, bytes, edited);
        using var reopened = CompiledModelIntegrity.Read(result, path);
        var checkedModel = (Model)reopened.DataBlock!;
        CompiledModelIntegrity.VerifyUneditedBlocks(source, bytes, reopened, result, edited);
        if (preservedModel != CompiledModelIntegrity.Fingerprint(checkedModel.Data, OmitMetadataText)
            || preservedMetadata != CompiledModelIntegrity.Fingerprint(checkedModel.KeyValues, OmitSoundSections)
            || checkedModel.KeyValues.ContainsKey(LegacySection)
            || !checkedModel.KeyValues.TryGetValue(CurrentSection, out var migrated)
            || CompiledModelIntegrity.Fingerprint(migrated) != expected)
            throw new InvalidDataException("Tagged-sound repair changed unrelated model data or authored sound settings.");
        return new(result, SoundSettingsRepairStatus.Repaired,
            $"Renamed {LegacySection} to {CurrentSection}; all {sounds.Count} sound-table references and unrelated model data preserved.");
    }

    private static bool OmitMetadataText(string path) => path == "m_modelInfo/m_keyValueText";
    private static bool OmitSoundSections(string path) => path is LegacySection or CurrentSection;
    private static bool HasMetadata(Model model) => model.Data.TryGetValue("m_modelInfo", out var info)
        && info.IsCollection && info.TryGetValue("m_keyValueText", out var text)
        && text.ValueType == KVValueType.String && !string.IsNullOrWhiteSpace((string)text);
}
