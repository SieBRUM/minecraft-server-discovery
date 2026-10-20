using System.Numerics;

namespace McDiscovery.Scanner.Targets;

/// <summary>
/// A keyed bijection on <c>[0, size)</c> (Feistel network with cycle walking). Scanning in this order spreads
/// the packets over all target networks instead of hammering one subnet at a time, the same idea as
/// masscan's BlackRock cipher, and needs no memory however large the range is.
/// </summary>
public sealed class Permutation
{
    private const int Rounds = 4;
    private readonly long _size;
    private readonly int _halfBits;
    private readonly ulong _halfMask;
    private readonly ulong[] _keys;

    public Permutation(long size, ulong seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        _size = size;
        var bits = Math.Max(2, 64 - BitOperations.LeadingZeroCount((ulong)(size - 1)));
        _halfBits = (bits + 1) / 2;
        _halfMask = (1UL << _halfBits) - 1;
        _keys = new ulong[Rounds];
        var state = seed;
        for (var i = 0; i < Rounds; i++)
        {
            _keys[i] = SplitMix64(ref state);
        }
    }

    public long Size => _size;

    /// <summary>Maps an index in <c>[0, size)</c> to a unique index in <c>[0, size)</c>.</summary>
    public long Map(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _size);

        // The Feistel domain is a power of two >= size (at most 4x larger), so this loop ends after a few
        // iterations on average; it always ends because the cipher is a permutation of its domain.
        var value = (ulong)index;
        do
        {
            value = Encrypt(value);
        }
        while (value >= (ulong)_size);

        return (long)value;
    }

    private ulong Encrypt(ulong value)
    {
        var left = value >> _halfBits;
        var right = value & _halfMask;
        for (var i = 0; i < Rounds; i++)
        {
            var next = left ^ (Round(right, _keys[i]) & _halfMask);
            left = right;
            right = next;
        }

        return (left << _halfBits) | right;
    }

    private static ulong Round(ulong value, ulong key)
    {
        var x = value ^ key;
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return x;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9e3779b97f4a7c15UL;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }
}
