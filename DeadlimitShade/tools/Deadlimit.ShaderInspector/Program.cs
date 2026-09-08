using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveResourceFormat.CompiledShader;

var options = ParseOptions(args);
var vpkPath = Path.GetFullPath(Required(options, "vpk"));
var resourcePath = Required(options, "resource").Replace('\\', '/').TrimStart('/');
var outputPath = Path.GetFullPath(Required(options, "output"));
var staticComboIds = ParseStaticComboIds(options.GetValueOrDefault("static-combos", "24"));
var writeDecompiled = options.ContainsKey("decompile");

Directory.CreateDirectory(outputPath);

using var package = new Package();
package.Read(vpkPath);
var entry = (package.Entries ?? throw new InvalidDataException("VPK has no entry table."))
    .SelectMany(group => group.Value)
    .Single(item => item.GetFullPath().Replace('\\', '/').Equals(resourcePath, StringComparison.OrdinalIgnoreCase));
package.ReadEntry(entry, out byte[] sourceBytes);

using var stream = new MemoryStream(sourceBytes, writable: false);
using var program = new VfxProgramData();
program.Read(resourcePath, stream);

var staticDefinitions = program.StaticComboArray.Select((definition, index) => DescribeDefinition(definition, index)).ToArray();
var dynamicDefinitions = program.DynamicComboArray.Select((definition, index) => DescribeDefinition(definition, index)).ToArray();
var availableStaticComboIds = program.StaticComboEntries.Keys.ToArray();

var selectedCombos = new List<object>();
foreach (var staticComboId in staticComboIds)
{
    if (!program.StaticComboEntries.ContainsKey(staticComboId))
    {
        throw new ArgumentOutOfRangeException(nameof(staticComboIds), staticComboId, "Static combo is absent from this VCS.");
    }

    var combo = program.GetStaticCombo(staticComboId);
    var staticValues = DecodeCombo(staticComboId, program.StaticComboArray);
    var dynamicStates = combo.DynamicCombos.Select(state => new
    {
        state.DynamicComboId,
        state.ShaderFileId,
        activeValues = DecodeCombo(state.DynamicComboId, program.DynamicComboArray),
        renderState = DescribeRenderState(state),
    }).ToArray();

    var shaderFiles = combo.ShaderFiles.Select((shader, index) =>
    {
        string? decompiledFile = null;
        if (writeDecompiled)
        {
            decompiledFile = $"static{staticComboId}-file{index}-id{shader.ShaderFileId}.decompiled.txt";
            File.WriteAllText(Path.Combine(outputPath, decompiledFile), shader.GetDecompiledFile());
        }

        return new
        {
            index,
            shader.ShaderFileId,
            shader.Size,
            bytecodeLength = shader.Bytecode.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(shader.Bytecode)),
            decompiledFile,
        };
    }).ToArray();

    selectedCombos.Add(new
    {
        staticComboId,
        activeValues = staticValues,
        dynamicStateCount = dynamicStates.Length,
        shaderFileCount = shaderFiles.Length,
        dynamicStates,
        shaderFiles,
    });
}

var report = new
{
    source = new
    {
        vpk = vpkPath,
        resource = resourcePath,
        length = sourceBytes.Length,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(sourceBytes)),
    },
    format = new
    {
        program.VcsVersion,
        program.VcsProgramType,
        program.VcsPlatformType,
        program.VcsShaderModelType,
    },
    staticDefinitions,
    dynamicDefinitions,
    availableStaticComboCount = availableStaticComboIds.Length,
    availableStaticComboIds,
    selectedCombos,
};

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var reportPath = Path.Combine(outputPath, "report.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine(reportPath);

static object DescribeDefinition(VfxCombo definition, int index) => new
{
    index,
    definition.Name,
    definition.AliasName,
    type = definition.ComboType.ToString(),
    stride = definition.CalculatedComboId,
    definition.RangeMin,
    definition.RangeMax,
    definition.ComboSourceType,
    definition.FeatureIndex,
    definition.FeatureComparisonValue,
    definition.Strings,
};

static object? DescribeRenderState(VfxRenderStateInfo state) =>
    state is VfxRenderStateInfoPixelShader pixel
        ? new
        {
            pixel.RasterizerStateDesc,
            pixel.DepthStencilStateDesc,
            pixel.BlendStateDesc,
        }
        : null;

static IReadOnlyDictionary<string, int> DecodeCombo(long comboId, IReadOnlyList<VfxCombo> definitions)
{
    var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var definition in definitions)
    {
        var stateCount = definition.RangeMax - definition.RangeMin + 1;
        var value = (int)((comboId / definition.CalculatedComboId) % stateCount) + definition.RangeMin;
        if (value != 0)
        {
            values.Add(definition.Name, value);
        }
    }

    return values;
}

static long[] ParseStaticComboIds(string text) => text
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(long.Parse)
    .Distinct()
    .Order()
    .ToArray();

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unexpected argument: {argument}");
        }

        var name = argument[2..];
        if (name.Equals("decompile", StringComparison.OrdinalIgnoreCase))
        {
            parsed[name] = "true";
            continue;
        }

        if (++index >= arguments.Length)
        {
            throw new ArgumentException($"Missing value for --{name}.");
        }

        parsed[name] = arguments[index];
    }

    return parsed;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required option --{name}.");
