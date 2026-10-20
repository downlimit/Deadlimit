using System.Security.Cryptography;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

internal static class CompiledModelCameraRepairService
{
    public static IReadOnlyList<string> RepairBuildOutputs(
        string addonGameRoot, string retailRoot, Action<string> log, CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(Path.Combine(retailRoot, "game", "citadel", "pak01_dir.vpk"));
        var references = package.Entries!.SelectMany(group => group.Value)
            .GroupBy(entry => entry.GetFullPath().Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var staged = new List<(string Path, byte[] Before, byte[] After)>();
        var warnings = new List<string>();
        var camerasRepaired = 0;
        var soundsRepaired = 0;
        foreach (var path in Directory.EnumerateFiles(addonGameRoot, "*.vmdl_c", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resourcePath = Path.GetRelativePath(addonGameRoot, path).Replace('\\', '/');
            if (!resourcePath.StartsWith("models/heroes", StringComparison.OrdinalIgnoreCase)) continue;
            if (!references.TryGetValue(resourcePath, out var matches))
            {
                log($"Camera skipped (no exact retail counterpart): {resourcePath}");
                continue;
            }
            if (matches.Length != 1) throw new InvalidDataException($"Ambiguous retail model: {resourcePath}");
            package.ReadEntry(matches[0], out byte[] retail);
            var before = File.ReadAllBytes(path);
            var result = CompiledModelCameraRepair.Repair(before, retail, resourcePath);
            log($"Camera {result.Status}: {resourcePath}: {result.Detail}");
            foreach (var remapping in result.RemappedBones) log($"  Camera bone adaptation: {remapping}");
            if (result.Status is CameraRepairStatus.ReviewNeeded or CameraRepairStatus.Unsupported)
                warnings.Add($"{resourcePath}: {result.Detail}");
            if (result.Modified) camerasRepaired++;
            var sound = CompiledModelSoundRepair.Repair(result.Bytes, retail, resourcePath);
            log($"Tagged sounds {sound.Status}: {resourcePath}: {sound.Detail}");
            if (sound.Status == SoundSettingsRepairStatus.ReviewNeeded)
                warnings.Add($"{resourcePath}: {sound.Detail}");
            if (sound.Modified) soundsRepaired++;
            if (result.Modified || sound.Modified) staged.Add((path, before, sound.Bytes));
        }
        var committed = new List<(string Path, byte[] Before)>();
        try
        {
            foreach (var edit in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SHA256.HashData(File.ReadAllBytes(edit.Path)).AsSpan().SequenceEqual(SHA256.HashData(edit.Before)))
                    throw new IOException($"Compiled model changed during compatibility repair: {edit.Path}");
                AtomicFile.WriteAllBytes(edit.Path, edit.After);
                committed.Add((edit.Path, edit.Before));
                if (!File.ReadAllBytes(edit.Path).AsSpan().SequenceEqual(edit.After))
                    throw new IOException($"Model compatibility repair write verification failed: {edit.Path}");
            }
        }
        catch
        {
            foreach (var edit in committed.AsEnumerable().Reverse()) AtomicFile.WriteAllBytes(edit.Path, edit.Before);
            throw;
        }
        log($"Camera models repaired: {camerasRepaired}");
        log($"Tagged-sound models repaired: {soundsRepaired}");
        return warnings;
    }
}
