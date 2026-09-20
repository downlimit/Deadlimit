using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static class LocalizedTextCatalog
{
    private const string ChineseResource = "Deadlimit.Localization.zh-CN.json";
    private const string PortugueseResource = "Deadlimit.Localization.pt-BR.json";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Chinese =
        new(() => Load(ChineseResource));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Portuguese =
        new(() => Load(PortugueseResource));

    public static string Translate(string language, string english, string? englishExpression)
    {
        var translations = string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? Chinese.Value
            : Portuguese.Value;

        if (translations.TryGetValue(english, out var exact))
        {
            return exact;
        }

        if (!string.IsNullOrWhiteSpace(englishExpression)
            && TryBuildTemplate(englishExpression, english, out var template, out var values)
            && translations.TryGetValue(template, out var translatedTemplate))
        {
            return ApplyTemplate(translatedTemplate, values);
        }

        return english;
    }

    internal static bool TryBuildTemplate(
        string expression,
        string renderedEnglish,
        out string template,
        out string[] values)
    {
        template = string.Empty;
        values = [];

        if (!TryParseExpression(expression, out var segments, out var placeholderCount)
            || placeholderCount == 0
            || segments.All(string.IsNullOrEmpty))
        {
            return false;
        }

        var builder = new StringBuilder(segments[0]);
        for (var index = 0; index < placeholderCount; index++)
        {
            builder.Append('{').Append(index).Append('}');
            builder.Append(segments[index + 1]);
        }
        template = builder.ToString();

        var pattern = new StringBuilder("^");
        pattern.Append(Regex.Escape(segments[0]));
        for (var index = 0; index < placeholderCount; index++)
        {
            pattern.Append("(?<p").Append(index).Append(@">[\s\S]*?)");
            pattern.Append(Regex.Escape(segments[index + 1]));
        }
        pattern.Append('$');

        var match = Regex.Match(renderedEnglish, pattern.ToString(), RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            template = string.Empty;
            return false;
        }

        values = Enumerable.Range(0, placeholderCount)
            .Select(index => match.Groups[$"p{index}"].Value)
            .ToArray();
        return true;
    }

    private static string ApplyTemplate(string template, IReadOnlyList<string> values)
    {
        var result = template;
        for (var index = values.Count - 1; index >= 0; index--)
        {
            result = result.Replace(
                $"{{{index}}}",
                values[index],
                StringComparison.Ordinal);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> Load(string resourceName)
    {
        using var stream = typeof(LocalizedTextCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded localization resource was not found: {resourceName}");
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Localization resource is empty: {resourceName}");
        return new Dictionary<string, string>(translations, StringComparer.Ordinal);
    }

    private static bool TryParseExpression(
        string expression,
        out List<string> segments,
        out int placeholderCount)
    {
        var parsedSegments = new List<string> { string.Empty };
        var parsedPlaceholderCount = 0;
        var index = 0;

        void AppendLiteral(string value) =>
            parsedSegments[^1] += value;

        void AppendPlaceholder()
        {
            parsedPlaceholderCount++;
            parsedSegments.Add(string.Empty);
        }

        while (index < expression.Length)
        {
            SkipSeparators(expression, ref index);
            if (index >= expression.Length)
            {
                break;
            }

            var interpolated = false;
            var verbatim = false;

            if (StartsWith(expression, index, "$@\"") || StartsWith(expression, index, "@$\""))
            {
                interpolated = true;
                verbatim = true;
                index += 3;
            }
            else if (StartsWith(expression, index, "$\""))
            {
                interpolated = true;
                index += 2;
            }
            else if (StartsWith(expression, index, "@\""))
            {
                verbatim = true;
                index += 2;
            }
            else if (expression[index] == '"')
            {
                index++;
            }
            else
            {
                var end = FindNextTopLevelPlus(expression, index);
                if (!string.IsNullOrWhiteSpace(expression[index..end]))
                {
                    AppendPlaceholder();
                }
                index = end < expression.Length ? end + 1 : end;
                continue;
            }

            var literal = new StringBuilder();
            while (index < expression.Length)
            {
                var current = expression[index];

                if (verbatim)
                {
                    if (current == '"' && index + 1 < expression.Length && expression[index + 1] == '"')
                    {
                        literal.Append('"');
                        index += 2;
                        continue;
                    }
                    if (current == '"')
                    {
                        index++;
                        break;
                    }
                }
                else
                {
                    if (current == '\\' && index + 1 < expression.Length)
                    {
                        literal.Append(DecodeEscape(expression, ref index));
                        continue;
                    }
                    if (current == '"')
                    {
                        index++;
                        break;
                    }
                }

                if (interpolated && current == '{')
                {
                    if (index + 1 < expression.Length && expression[index + 1] == '{')
                    {
                        literal.Append('{');
                        index += 2;
                        continue;
                    }

                    AppendLiteral(literal.ToString());
                    literal.Clear();
                    SkipInterpolation(expression, ref index);
                    AppendPlaceholder();
                    continue;
                }

                if (interpolated
                    && current == '}'
                    && index + 1 < expression.Length
                    && expression[index + 1] == '}')
                {
                    literal.Append('}');
                    index += 2;
                    continue;
                }

                literal.Append(current);
                index++;
            }

            AppendLiteral(literal.ToString());
        }

        segments = parsedSegments;
        placeholderCount = parsedPlaceholderCount;
        return true;
    }

    private static void SkipSeparators(string expression, ref int index)
    {
        while (index < expression.Length
            && (char.IsWhiteSpace(expression[index]) || expression[index] == '+'))
        {
            index++;
        }
    }

    private static int FindNextTopLevelPlus(string expression, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < expression.Length; index++)
        {
            var current = expression[index];
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
                continue;
            }

            if (current is '(' or '[' or '{')
            {
                depth++;
                continue;
            }
            if (current is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (current == '+' && depth == 0)
            {
                return index;
            }
        }

        return expression.Length;
    }

    private static void SkipInterpolation(string expression, ref int index)
    {
        var depth = 1;
        index++;
        var inString = false;
        var escaped = false;

        while (index < expression.Length && depth > 0)
        {
            var current = expression[index];
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
                index++;
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
            else if (current == '}')
            {
                depth--;
            }
            index++;
        }
    }

    private static string DecodeEscape(string expression, ref int index)
    {
        index++;
        if (index >= expression.Length)
        {
            return "\\";
        }

        var escaped = expression[index++];
        return escaped switch
        {
            '\\' => "\\",
            '"' => "\"",
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            '0' => "\0",
            'a' => "\a",
            'b' => "\b",
            'f' => "\f",
            'v' => "\v",
            _ => escaped.ToString(),
        };
    }

    internal static int RunSmoke()
    {
        var chinese = Chinese.Value;
        var portuguese = Portuguese.Value;
        if (chinese.Count == 0 || portuguese.Count == 0)
        {
            return 1;
        }

        if (!chinese.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                portuguese.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            return 2;
        }

        if (chinese.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            || portuguese.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            return 3;
        }

        const string sampleExpression = "$\"Installed CSDK {installed}; CSDK {available} is available.\"";
        if (!TryBuildTemplate(
                sampleExpression,
                "Installed CSDK 12; CSDK 13 is available.",
                out var template,
                out var values)
            || template != "Installed CSDK {0}; CSDK {1} is available."
            || values.Length != 2
            || values[0] != "12"
            || values[1] != "13")
        {
            return 4;
        }

        var requiredKeys = new[]
        {
            "SETTINGS",
            "PREPARE FOR CSDK",
            "BUILD FOR TEST",
            "Interface language",
            "Installed CSDK {0}; CSDK {1} is available.",
        };
        foreach (var key in requiredKeys)
        {
            if (!chinese.TryGetValue(key, out var zh)
                || !portuguese.TryGetValue(key, out var pt)
                || string.IsNullOrWhiteSpace(zh)
                || string.IsNullOrWhiteSpace(pt))
            {
                return 5;
            }
        }

        return 0;
    }

    private static bool StartsWith(string value, int index, string token) =>
        index + token.Length <= value.Length
        && string.CompareOrdinal(value, index, token, 0, token.Length) == 0;
}
