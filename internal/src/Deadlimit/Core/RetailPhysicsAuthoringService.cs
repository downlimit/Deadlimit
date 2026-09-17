using System.Globalization;
using System.Numerics;
using System.Text;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed record RetailPhysicsInitializationResult(int JointCount, bool Added);

public static class RetailPhysicsAuthoringService
{
    private const string PhysicsJointListClass = "PhysicsJointList";

    public static RetailPhysicsInitializationResult EnsureRetailJoints(
        ProjectManifest manifest,
        string destinationVmdlPath,
        bool replaceExisting)
    {
        if (!replaceExisting && RetailVmdlInheritance.ContainsRootNode(destinationVmdlPath, PhysicsJointListClass))
        {
            return new RetailPhysicsInitializationResult(0, false);
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
            .Select(value => ReadConicalJoint(value, physics.BoneNames))
            .ToArray();
        var nodeText = CreatePhysicsJointList(joints);
        RetailVmdlInheritance.UpsertRootNode(destinationVmdlPath, PhysicsJointListClass, nodeText);
        return new RetailPhysicsInitializationResult(joints.Length, true);
    }

    private static RetailConicalJoint ReadConicalJoint(KVObject value, IReadOnlyList<string> boneNames)
    {
        var type = value["m_nType"].ToInt32(CultureInfo.InvariantCulture);
        if (type != 4)
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
        return new RetailConicalJoint(
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

    private static string CreatePhysicsJointList(IReadOnlyList<RetailConicalJoint> joints)
    {
        var text = new StringBuilder();
        text.AppendLine("{");
        text.AppendLine("\t_class = \"PhysicsJointList\"");
        text.AppendLine("\tchildren =");
        text.AppendLine("\t[");
        text.AppendLine("\t\t{");
        text.AppendLine("\t\t\t_class = \"Folder\"");
        text.AppendLine("\t\t\tname = \"Retail ragdoll joints\"");
        text.AppendLine("\t\t\tchildren =");
        text.AppendLine("\t\t\t[");
        foreach (var joint in joints)
        {
            text.AppendLine("\t\t\t\t{");
            text.AppendLine("\t\t\t\t\t_class = \"PhysicsJointConical\"");
            text.AppendLine($"\t\t\t\t\tparent_body = \"{EscapeKv3(joint.ParentBody)}\"");
            text.AppendLine($"\t\t\t\t\tchild_body = \"{EscapeKv3(joint.ChildBody)}\"");
            text.AppendLine($"\t\t\t\t\tanchor_origin = {FormatVector(joint.AnchorOrigin)}");
            text.AppendLine($"\t\t\t\t\tanchor_angles = {FormatVector(joint.AnchorAngles)}");
            text.AppendLine($"\t\t\t\t\tcollision_enabled = {FormatBoolean(joint.CollisionEnabled)}");
            text.AppendLine($"\t\t\t\t\tfriction = {FormatFloat(joint.Friction)}");
            text.AppendLine($"\t\t\t\t\tenable_swing_limit = {FormatBoolean(joint.EnableSwingLimit)}");
            text.AppendLine($"\t\t\t\t\tswing_limit = {FormatFloat(joint.SwingLimit)}");
            text.AppendLine("\t\t\t\t\tswing_offset_angle = [ 0.0, 0.0, 0.0 ]");
            text.AppendLine($"\t\t\t\t\tenable_twist_limit = {FormatBoolean(joint.EnableTwistLimit)}");
            text.AppendLine($"\t\t\t\t\tmin_twist_angle = {FormatFloat(joint.MinTwistAngle)}");
            text.AppendLine($"\t\t\t\t\tmax_twist_angle = {FormatFloat(joint.MaxTwistAngle)}");
            text.AppendLine("\t\t\t\t},");
        }
        text.AppendLine("\t\t\t]");
        text.AppendLine("\t\t},");
        text.AppendLine("\t]");
        text.Append('}');
        return text.ToString();
    }

    private static string FormatVector(Vector3 value) =>
        $"[ {FormatFloat(value.X)}, {FormatFloat(value.Y)}, {FormatFloat(value.Z)} ]";

    private static string FormatFloat(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static double RadiansToDegrees(float value) => value * (180.0 / Math.PI);

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string EscapeKv3(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record RetailConicalJoint(
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
}
