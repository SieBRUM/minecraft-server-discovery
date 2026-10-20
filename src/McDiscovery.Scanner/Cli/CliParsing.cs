using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace McDiscovery.Scanner.Cli;

internal static partial class CliParsing
{
    /// <summary>Parses "500ms", "30s", "15m", "6h", "7d" or a TimeSpan like "01:30:00".</summary>
    public static bool TryParseDuration(string text, out TimeSpan value)
    {
        var match = DurationRegex().Match(text.Trim());
        if (match.Success)
        {
            var amount = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            value = match.Groups[2].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            return value >= TimeSpan.Zero;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value) && value >= TimeSpan.Zero;
    }

    public static TimeSpan ParseDuration(ArgumentResult result)
    {
        var token = result.Tokens.Count > 0 ? result.Tokens[0].Value : "";
        if (TryParseDuration(token, out var value))
        {
            return value;
        }

        result.AddError($"'{token}' is not a valid duration (examples: 500ms, 30s, 15m, 6h, 7d)");
        return TimeSpan.Zero;
    }

    /// <summary>Parses "25565", "25565,25566" or "25560-25570" (and combinations).</summary>
    public static bool TryParsePorts(string text, out List<ushort> ports)
    {
        ports = [];
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (!ushort.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var single) || single == 0)
                {
                    return false;
                }

                ports.Add(single);
                continue;
            }

            if (!ushort.TryParse(part.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var from) ||
                !ushort.TryParse(part.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var to) ||
                from == 0 || to < from)
            {
                return false;
            }

            for (var p = from; p <= to; p++)
            {
                ports.Add(p);
                if (p == ushort.MaxValue)
                {
                    break;
                }
            }
        }

        ports = [.. ports.Distinct().Order()];
        return true;
    }

    public static List<ushort> ParsePorts(ArgumentResult result)
    {
        var token = string.Join(',', result.Tokens.Select(t => t.Value));
        if (TryParsePorts(token, out var ports))
        {
            return ports;
        }

        result.AddError($"'{token}' is not a valid port list (examples: 25565, 25565,25566, 25560-25570, none)");
        return [];
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(ms|s|m|h|d)$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    public static partial Regex UsernameRegex();
}
