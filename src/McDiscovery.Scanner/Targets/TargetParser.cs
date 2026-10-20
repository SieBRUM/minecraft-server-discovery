using System.Globalization;

namespace McDiscovery.Scanner.Targets;

/// <summary>
/// Parses target / exclude specifications. Accepted forms, one per line or comma separated:
/// <c>1.2.3.4</c>, <c>1.2.3.0/24</c>, <c>1.2.3.4-1.2.3.200</c>. <c>#</c> starts a comment.
/// </summary>
public static class TargetParser
{
    public static IpRange Parse(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var s = spec.Trim();

        if (s.Contains('/', StringComparison.Ordinal))
        {
            return ParseCidr(s);
        }

        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            if (!IpV4.TryParse(s.AsSpan(0, dash).Trim(), out var start) || !IpV4.TryParse(s.AsSpan(dash + 1).Trim(), out var end))
            {
                throw new FormatException($"Invalid IPv4 range '{spec}'");
            }

            if (end < start)
            {
                throw new FormatException($"Range '{spec}' ends before it starts");
            }

            return new IpRange(start, end);
        }

        if (!IpV4.TryParse(s, out var single))
        {
            throw new FormatException($"Invalid IPv4 address '{spec}' (only IPv4 is supported)");
        }

        return new IpRange(single, single);
    }

    public static IpRange ParseCidr(string cidr)
    {
        var slash = cidr.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0 || !IpV4.TryParse(cidr.AsSpan(0, slash).Trim(), out var address) ||
            !int.TryParse(cidr.AsSpan(slash + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) ||
            prefix is < 0 or > 32)
        {
            throw new FormatException($"Invalid IPv4 CIDR '{cidr}'");
        }

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var start = address & mask;
        return new IpRange(start, start | ~mask);
    }

    /// <summary>Parses free text (a file or a command line value). Errors include the line number.</summary>
    public static List<IpRange> ParseText(string text, string sourceName)
    {
        var result = new List<IpRange>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            foreach (var token in line.Split([',', ' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    result.Add(Parse(token));
                }
                catch (FormatException e)
                {
                    throw new FormatException($"{sourceName}:{lineNumber}: {e.Message}", e);
                }
            }
        }

        return result;
    }

    public static List<IpRange> ParseFile(string path) => ParseText(File.ReadAllText(path), path);
}
