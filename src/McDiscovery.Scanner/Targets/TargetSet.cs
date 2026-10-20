using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace McDiscovery.Scanner.Targets;

/// <summary>Inclusive IPv4 range.</summary>
public readonly record struct IpRange(uint Start, uint End)
{
    public long Count => (long)End - Start + 1;

    public override string ToString() => Start == End
        ? IpV4.Format(Start)
        : $"{IpV4.Format(Start)}-{IpV4.Format(End)}";
}

public static class IpV4
{
    public static uint ToUInt(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new FormatException($"'{address}' is not an IPv4 address");
        }

        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    public static IPAddress ToAddress(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    public static string Format(uint value) => string.Create(CultureInfo.InvariantCulture,
        $"{value >> 24}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}");

    /// <summary>Strict dotted-quad parser (IPAddress.Parse also accepts forms like "1.2" or "0x7f.1").</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        var parts = 0;
        Span<Range> ranges = stackalloc Range[5];
        var count = text.Split(ranges, '.');
        if (count != 4)
        {
            return false;
        }

        foreach (var r in ranges[..4])
        {
            var part = text[r];
            if (part.Length is 0 or > 3 || !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) || octet > 255)
            {
                return false;
            }

            value = (value << 8) | octet;
            parts++;
        }

        return parts == 4;
    }
}

/// <summary>
/// A set of IPv4 addresses stored as sorted, merged, disjoint ranges. Supports random access by index so the
/// scanners can walk it in a pseudo-random order without materialising billions of addresses.
/// </summary>
public sealed class TargetSet
{
    /// <summary>Never scanned, whatever the configuration says: "this network", loopback, link-local, multicast and reserved/broadcast.</summary>
    public static readonly IReadOnlyList<IpRange> MandatoryExcludes =
    [
        TargetParser.ParseCidr("0.0.0.0/8"),
        TargetParser.ParseCidr("127.0.0.0/8"),
        TargetParser.ParseCidr("169.254.0.0/16"),
        TargetParser.ParseCidr("224.0.0.0/4"),
        TargetParser.ParseCidr("240.0.0.0/4"),
    ];

    private readonly IpRange[] _ranges;
    private readonly long[] _offsets; // _offsets[i] = number of addresses before _ranges[i]

    private TargetSet(IpRange[] ranges)
    {
        _ranges = ranges;
        _offsets = new long[ranges.Length];
        long total = 0;
        for (var i = 0; i < ranges.Length; i++)
        {
            _offsets[i] = total;
            total += ranges[i].Count;
        }

        Count = total;
    }

    public long Count { get; }

    public IReadOnlyList<IpRange> Ranges => _ranges;

    /// <summary>Builds <c>include - exclude - MandatoryExcludes</c>.</summary>
    public static TargetSet Create(IEnumerable<IpRange> include, IEnumerable<IpRange> exclude) =>
        Create(include, exclude, applyMandatoryExcludes: true);

    /// <summary>Tests use this to scan fake servers on loopback.</summary>
    internal static TargetSet Create(IEnumerable<IpRange> include, IEnumerable<IpRange> exclude, bool applyMandatoryExcludes)
    {
        var inc = Merge(include);
        var exc = Merge(applyMandatoryExcludes ? exclude.Concat(MandatoryExcludes) : exclude);
        return new TargetSet(Subtract(inc, exc));
    }

    public uint this[long index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var i = Array.BinarySearch(_offsets, index);
            if (i < 0)
            {
                i = ~i - 1;
            }

            return (uint)(_ranges[i].Start + (index - _offsets[i]));
        }
    }

    public bool Contains(uint address)
    {
        int lo = 0, hi = _ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (address < _ranges[mid].Start)
            {
                hi = mid - 1;
            }
            else if (address > _ranges[mid].End)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    internal static IpRange[] Merge(IEnumerable<IpRange> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<IpRange>(sorted.Count);
        foreach (var r in sorted)
        {
            if (merged.Count > 0 && (long)r.Start <= (long)merged[^1].End + 1)
            {
                if (r.End > merged[^1].End)
                {
                    merged[^1] = merged[^1] with { End = r.End };
                }
            }
            else
            {
                merged.Add(r);
            }
        }

        return [.. merged];
    }

    internal static IpRange[] Subtract(IpRange[] include, IpRange[] exclude)
    {
        var result = new List<IpRange>();
        var j = 0;
        foreach (var range in include)
        {
            long start = range.Start;
            long end = range.End;
            while (j < exclude.Length && exclude[j].End < start)
            {
                j++;
            }

            var k = j;
            while (start <= end && k < exclude.Length && exclude[k].Start <= end)
            {
                if (exclude[k].Start > start)
                {
                    result.Add(new IpRange((uint)start, exclude[k].Start - 1));
                }

                start = Math.Max(start, (long)exclude[k].End + 1);
                k++;
            }

            if (start <= end)
            {
                result.Add(new IpRange((uint)start, (uint)end));
            }
        }

        return [.. result];
    }
}
