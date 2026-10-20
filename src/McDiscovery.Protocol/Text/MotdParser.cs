using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McDiscovery.Protocol.Text;

/// <summary>
/// A run of text with one style. The website renders these as plain text nodes, so the content never needs
/// escaping; <see cref="Color"/> is always either <c>null</c> or a validated <c>#rrggbb</c> value.
/// </summary>
public sealed record MotdSegment(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("bold")] bool Bold = false,
    [property: JsonPropertyName("italic")] bool Italic = false,
    [property: JsonPropertyName("underlined")] bool Underlined = false,
    [property: JsonPropertyName("strikethrough")] bool Strikethrough = false,
    [property: JsonPropertyName("obfuscated")] bool Obfuscated = false);

public sealed record ParsedText(string Plain, IReadOnlyList<MotdSegment> Segments)
{
    public static readonly ParsedText Empty = new("", []);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string SegmentsToJson() => JsonSerializer.Serialize(Segments, JsonOptions);
}

/// <summary>
/// Parses Minecraft text (JSON chat components and legacy <c>§</c> formatting codes) into plain text and
/// styled segments. Input comes from untrusted servers, so length, depth and segment count are capped.
/// </summary>
public static class MotdParser
{
    public const int MaxTextLength = 2048;
    public const int MaxSegments = 256;
    private const int MaxDepth = 24;

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000",
        ["dark_blue"] = "#0000aa",
        ["dark_green"] = "#00aa00",
        ["dark_aqua"] = "#00aaaa",
        ["dark_red"] = "#aa0000",
        ["dark_purple"] = "#aa00aa",
        ["gold"] = "#ffaa00",
        ["gray"] = "#aaaaaa",
        ["dark_gray"] = "#555555",
        ["blue"] = "#5555ff",
        ["green"] = "#55ff55",
        ["aqua"] = "#55ffff",
        ["red"] = "#ff5555",
        ["light_purple"] = "#ff55ff",
        ["yellow"] = "#ffff55",
        ["white"] = "#ffffff",
    };

    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    private static readonly string[] LegacyColors =
    [
        "#000000", "#0000aa", "#00aa00", "#00aaaa", "#aa0000", "#aa00aa", "#ffaa00", "#aaaaaa",
        "#555555", "#5555ff", "#55ff55", "#55ffff", "#ff5555", "#ff55ff", "#ffff55", "#ffffff",
    ];

    /// <summary>Parses a status <c>description</c> element (string, component object or component array).</summary>
    public static ParsedText FromJson(JsonElement element)
    {
        var builder = new Builder();
        builder.AppendComponent(element, Style.Default, 0);
        return builder.Build();
    }

    /// <summary>Parses a JSON chat component string; falls back to legacy text when it isn't valid JSON.</summary>
    public static ParsedText FromJsonString(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return ParsedText.Empty;
        }

        var trimmed = json.AsSpan().TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[' || trimmed[0] == '"'))
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
                return FromJson(doc.RootElement);
            }
            catch (JsonException)
            {
                // Not JSON after all.
            }
        }

        return FromLegacy(json);
    }

    /// <summary>Parses text that uses legacy <c>§</c> formatting codes.</summary>
    public static ParsedText FromLegacy(string? text)
    {
        var builder = new Builder();
        builder.AppendLegacy(text ?? "", Style.Default);
        return builder.Build();
    }

    /// <summary>Removes control characters (except newlines) and caps the length.</summary>
    public static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sb = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var rune in value.EnumerateRunes())
        {
            if (sb.Length >= maxLength)
            {
                break;
            }

            if (rune.Value == '\n' || (!Rune.IsControl(rune) && rune.Value != 0xFFFD &&
                Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format))
            {
                if (sb.Length + rune.Utf16SequenceLength > maxLength)
                {
                    break;
                }

                sb.Append(rune.ToString());
            }
        }

        return sb.ToString();
    }

    internal static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return null;
        }

        if (NamedColors.TryGetValue(color, out var hex))
        {
            return hex;
        }

        if (color.Length == 7 && color[0] == '#' && !color.AsSpan(1).ContainsAnyExcept(HexDigits))
        {
            return color.ToLowerInvariant();
        }

        return null;
    }

    private readonly record struct Style(string? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough, bool Obfuscated)
    {
        public static readonly Style Default = new(null, false, false, false, false, false);
    }

    private sealed class Builder
    {
        private readonly List<MotdSegment> _segments = [];
        private readonly StringBuilder _plain = new();
        private bool _full;

        public ParsedText Build()
        {
            var plain = _plain.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            return new ParsedText(plain, _segments);
        }

        public void AppendComponent(JsonElement element, Style parent, int depth)
        {
            if (_full || depth > MaxDepth)
            {
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    AppendLegacy(element.GetString() ?? "", parent);
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    AppendText(element.GetRawText(), parent);
                    break;
                case JsonValueKind.Array:
                    // Vanilla semantics: the first element is the parent, the rest inherit its style.
                    Style? first = null;
                    foreach (var child in element.EnumerateArray())
                    {
                        if (first is null)
                        {
                            first = child.ValueKind == JsonValueKind.Object ? ApplyStyle(child, parent) : parent;
                            AppendComponent(child, parent, depth + 1);
                        }
                        else
                        {
                            AppendComponent(child, first.Value, depth + 1);
                        }
                    }

                    break;
                case JsonValueKind.Object:
                    AppendObject(element, parent, depth);
                    break;
            }
        }

        private void AppendObject(JsonElement element, Style parent, int depth)
        {
            var style = ApplyStyle(element, parent);

            if (element.TryGetProperty("text", out var text))
            {
                AppendComponent(text, style, depth + 1);
            }
            else if (element.TryGetProperty("", out var unnamed))
            {
                // NBT-derived components (1.20.3+) can put the text under an empty key.
                AppendComponent(unnamed, style, depth + 1);
            }
            else if (element.TryGetProperty("translate", out var translate))
            {
                var fallback = element.TryGetProperty("fallback", out var fb) && fb.ValueKind == JsonValueKind.String
                    ? fb.GetString()
                    : null;
                AppendText(fallback ?? (translate.ValueKind == JsonValueKind.String ? translate.GetString() ?? "" : ""), style);
            }

            if (element.TryGetProperty("extra", out var extra))
            {
                if (extra.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in extra.EnumerateArray())
                    {
                        AppendComponent(child, style, depth + 1);
                    }
                }
                else
                {
                    AppendComponent(extra, style, depth + 1);
                }
            }
        }

        private static Style ApplyStyle(JsonElement element, Style parent)
        {
            var style = parent;
            if (element.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String)
            {
                var normalized = NormalizeColor(color.GetString());
                if (normalized is not null || string.Equals(color.GetString(), "reset", StringComparison.OrdinalIgnoreCase))
                {
                    style = style with { Color = normalized };
                }
            }

            style = style with
            {
                Bold = ReadFlag(element, "bold") ?? style.Bold,
                Italic = ReadFlag(element, "italic") ?? style.Italic,
                Underlined = ReadFlag(element, "underlined") ?? style.Underlined,
                Strikethrough = ReadFlag(element, "strikethrough") ?? style.Strikethrough,
                Obfuscated = ReadFlag(element, "obfuscated") ?? style.Obfuscated,
            };
            return style;
        }

        private static bool? ReadFlag(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.TryGetInt32(out var i) ? i != 0 : null,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : null,
                _ => null,
            };
        }

        public void AppendLegacy(string text, Style baseStyle)
        {
            var style = baseStyle;
            var run = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '§' || i + 1 >= text.Length)
                {
                    run.Append(c);
                    continue;
                }

                var code = char.ToLowerInvariant(text[i + 1]);

                // Bukkit hex colour: §x§r§r§g§g§b§b
                if (code == 'x' && i + 13 < text.Length && TryReadBukkitHex(text, i + 2, out var hex))
                {
                    Flush(run, style);
                    style = Style.Default with { Color = hex };
                    i += 13;
                    continue;
                }

                var colorIndex = "0123456789abcdef".IndexOf(code, StringComparison.Ordinal);
                if (colorIndex >= 0)
                {
                    Flush(run, style);
                    style = Style.Default with { Color = LegacyColors[colorIndex] }; // colour codes reset formatting
                    i++;
                    continue;
                }

                Style? next = code switch
                {
                    'k' => style with { Obfuscated = true },
                    'l' => style with { Bold = true },
                    'm' => style with { Strikethrough = true },
                    'n' => style with { Underlined = true },
                    'o' => style with { Italic = true },
                    'r' => baseStyle,
                    _ => null,
                };

                if (next is null)
                {
                    run.Append(c); // unknown code: keep the character
                    continue;
                }

                Flush(run, style);
                style = next.Value;
                i++;
            }

            Flush(run, style);
        }

        private static bool TryReadBukkitHex(string text, int start, out string hex)
        {
            Span<char> digits = stackalloc char[6];
            for (var n = 0; n < 6; n++)
            {
                var pos = start + (n * 2);
                if (text[pos] != '§' || !char.IsAsciiHexDigit(text[pos + 1]))
                {
                    hex = "";
                    return false;
                }

                digits[n] = char.ToLowerInvariant(text[pos + 1]);
            }

            hex = "#" + new string(digits);
            return true;
        }

        private void Flush(StringBuilder run, Style style)
        {
            if (run.Length == 0)
            {
                return;
            }

            AppendText(run.ToString(), style);
            run.Clear();
        }

        private void AppendText(string raw, Style style)
        {
            if (_full)
            {
                return;
            }

            var remaining = MaxTextLength - _plain.Length;
            var text = Sanitize(raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), remaining);
            if (text.Length == 0)
            {
                _full = remaining <= 0;
                return;
            }

            _plain.Append(text);

            var segment = new MotdSegment(text, style.Color, style.Bold, style.Italic, style.Underlined, style.Strikethrough, style.Obfuscated);
            if (_segments.Count > 0 && _segments[^1] with { Text = text } == segment)
            {
                _segments[^1] = _segments[^1] with { Text = _segments[^1].Text + text };
            }
            else if (_segments.Count < MaxSegments)
            {
                _segments.Add(segment);
            }
            else
            {
                _segments[^1] = _segments[^1] with { Text = _segments[^1].Text + text };
            }

            if (_plain.Length >= MaxTextLength)
            {
                _full = true;
            }
        }
    }
}
