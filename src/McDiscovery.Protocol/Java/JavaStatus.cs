using System.Text.Json;
using McDiscovery.Protocol.Text;

namespace McDiscovery.Protocol.Java;

public sealed record PlayerSample(string Name, string? Uuid);

public sealed record ModInfo(string Id, string? Version);

/// <summary>Everything a Java Edition server reveals in its server list ping.</summary>
public sealed record JavaStatus
{
    public string? VersionName { get; init; }
    public int? ProtocolVersion { get; init; }
    public int? PlayersOnline { get; init; }
    public int? PlayersMax { get; init; }
    public IReadOnlyList<PlayerSample> Sample { get; init; } = [];
    public ParsedText Motd { get; init; } = ParsedText.Empty;

    /// <summary>Raw PNG bytes of the server icon (validated PNG signature).</summary>
    public byte[]? Favicon { get; init; }

    public bool? EnforcesSecureChat { get; init; }
    public bool? PreviewsChat { get; init; }
    public bool? PreventsChatReports { get; init; }
    public string? ModLoader { get; init; }
    public IReadOnlyList<ModInfo> Mods { get; init; } = [];
    public int? LatencyMs { get; init; }

    /// <summary><c>true</c> when the status came from the pre-1.7 legacy ping.</summary>
    public bool IsLegacy { get; init; }
}

public static class JavaStatusParser
{
    public const int MaxFaviconBytes = 256 * 1024;
    public const int MaxSamplePlayers = 64;
    public const int MaxMods = 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Parses the JSON from a status response. Throws <see cref="ProtocolException"/> if it isn't a JSON object.</summary>
    public static JavaStatus Parse(string json, int? latencyMs = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            throw new ProtocolException("Status is not valid JSON: " + e.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ProtocolException("Status JSON is not an object");
            }

            string? versionName = null;
            int? protocol = null;
            if (TryGetObject(root, "version", out var version))
            {
                versionName = GetString(version, "name") is { } name ? MotdParser.FromLegacy(name).Plain : null;
                protocol = GetInt(version, "protocol");
            }

            int? online = null, max = null;
            var sample = new List<PlayerSample>();
            if (TryGetObject(root, "players", out var players))
            {
                online = GetInt(players, "online");
                max = GetInt(players, "max");
                if (players.TryGetProperty("sample", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in list.EnumerateArray())
                    {
                        if (sample.Count >= MaxSamplePlayers)
                        {
                            break;
                        }

                        if (p.ValueKind == JsonValueKind.Object && GetString(p, "name") is { } playerName)
                        {
                            sample.Add(new PlayerSample(playerName, GetString(p, "id")));
                        }
                    }
                }
            }

            var motd = root.TryGetProperty("description", out var description)
                ? MotdParser.FromJson(description)
                : ParsedText.Empty;

            var (modLoader, mods) = ReadMods(root);

            return new JavaStatus
            {
                VersionName = versionName is null ? null : MotdParser.Sanitize(versionName, 256),
                ProtocolVersion = protocol,
                PlayersOnline = online,
                PlayersMax = max,
                Sample = sample,
                Motd = motd,
                Favicon = DecodeFavicon(GetString(root, "favicon")),
                EnforcesSecureChat = GetBool(root, "enforcesSecureChat"),
                PreviewsChat = GetBool(root, "previewsChat"),
                PreventsChatReports = GetBool(root, "preventsChatReports"),
                ModLoader = modLoader,
                Mods = mods,
                LatencyMs = latencyMs,
            };
        }
    }

    /// <summary>Decodes a <c>data:image/png;base64,</c> favicon. Returns <c>null</c> unless it really is a PNG.</summary>
    public static byte[]? DecodeFavicon(string? favicon)
    {
        const string prefix = "data:image/png;base64,";
        if (favicon is null || !favicon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var base64 = favicon.AsSpan(prefix.Length);
        if (base64.Length > MaxFaviconBytes * 4 / 3 + 8)
        {
            return null;
        }

        // Some servers wrap the base64 in newlines, which Convert accepts; anything else is rejected.
        var buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64Chars(base64, buffer, out var written) || written < PngSignature.Length)
        {
            return null;
        }

        return buffer.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature) ? buffer[..written] : null;
    }

    private static (string? Loader, IReadOnlyList<ModInfo> Mods) ReadMods(JsonElement root)
    {
        var mods = new List<ModInfo>();

        // Forge 1.7 - 1.12
        if (TryGetObject(root, "modinfo", out var modinfo))
        {
            if (modinfo.TryGetProperty("modList", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in list.EnumerateArray())
                {
                    if (mods.Count >= MaxMods)
                    {
                        break;
                    }

                    if (m.ValueKind == JsonValueKind.Object && GetString(m, "modid") is { } id)
                    {
                        mods.Add(new ModInfo(Clean(id), GetString(m, "version") is { } v ? Clean(v) : null));
                    }
                }
            }

            return ("Forge", mods);
        }

        // Forge 1.13+
        if (TryGetObject(root, "forgeData", out var forgeData))
        {
            if (forgeData.TryGetProperty("mods", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
            {
                foreach (var m in list.EnumerateArray())
                {
                    if (mods.Count >= MaxMods)
                    {
                        break;
                    }

                    if (m.ValueKind == JsonValueKind.Object && GetString(m, "modId") is { } id)
                    {
                        var marker = GetString(m, "modmarker");
                        mods.Add(new ModInfo(Clean(id), marker is null || ForgeDataDecoder.IsServerOnlyMarker(marker) ? null : Clean(marker)));
                    }
                }
            }
            else if (GetString(forgeData, "d") is { } encoded)
            {
                // 1.18+: mod list packed into a string. Best effort; a broken list is not fatal.
                foreach (var m in ForgeDataDecoder.TryDecode(encoded))
                {
                    if (mods.Count >= MaxMods)
                    {
                        break;
                    }

                    mods.Add(new ModInfo(Clean(m.Id), m.Version is null ? null : Clean(m.Version)));
                }
            }

            return ("Forge", mods);
        }

        // NeoForge advertises itself with isModded.
        if (GetBool(root, "isModded") == true)
        {
            return ("NeoForge", mods);
        }

        return (null, mods);
    }

    private static string Clean(string value) => MotdParser.Sanitize(value, 128);

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i))
            {
                return i;
            }

            // Some servers send absurd or fractional numbers; clamp instead of failing.
            if (v.TryGetDouble(out var d) && double.IsFinite(d))
            {
                return (int)Math.Clamp(d, int.MinValue, int.MaxValue);
            }
        }

        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
