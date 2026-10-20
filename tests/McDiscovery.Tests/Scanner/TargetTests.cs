using McDiscovery.Scanner.Cli;
using McDiscovery.Scanner.Targets;

namespace McDiscovery.Tests.Scanner;

public class TargetTests
{
    private static uint Ip(string s) => IpV4.TryParse(s, out var v) ? v : throw new FormatException(s);

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4", "1.2.3.4")]
    [InlineData("1.2.3.0/24", "1.2.3.0", "1.2.3.255")]
    [InlineData("1.2.3.77/24", "1.2.3.0", "1.2.3.255")]
    [InlineData("0.0.0.0/0", "0.0.0.0", "255.255.255.255")]
    [InlineData("10.0.0.1/32", "10.0.0.1", "10.0.0.1")]
    [InlineData(" 5.5.5.5 - 5.5.5.9 ", "5.5.5.5", "5.5.5.9")]
    public void Parses_specs(string spec, string start, string end)
    {
        Assert.Equal(new IpRange(Ip(start), Ip(end)), TargetParser.Parse(spec));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.256")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/-1")]
    [InlineData("::1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("1.2.3.9-1.2.3.1")]
    [InlineData("example.com")]
    [InlineData("1.2.3.04444")]
    public void Rejects_invalid_specs(string spec)
    {
        Assert.Throws<FormatException>(() => TargetParser.Parse(spec));
    }

    [Fact]
    public void Parses_files_with_comments()
    {
        var ranges = TargetParser.ParseText("# comment\n1.1.1.1, 2.2.2.0/31 # inline\r\n\n3.3.3.3-3.3.3.4\n", "test");
        Assert.Equal(3, ranges.Count);
        var error = Assert.Throws<FormatException>(() => TargetParser.ParseText("1.1.1.1\nbogus\n", "file.txt"));
        Assert.StartsWith("file.txt:2:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merges_and_subtracts()
    {
        var set = TargetSet.Create(
            [TargetParser.Parse("10.0.0.0/24"), TargetParser.Parse("10.0.0.128/25"), TargetParser.Parse("10.0.1.0/24"), TargetParser.Parse("20.0.0.1")],
            [TargetParser.Parse("10.0.0.10-10.0.0.19"), TargetParser.Parse("10.0.1.255"), TargetParser.Parse("9.0.0.0/8")]);

        Assert.Equal(512 - 10 - 1 + 1, set.Count);
        Assert.Equal(
            ["10.0.0.0-10.0.0.9", "10.0.0.20-10.0.1.254", "20.0.0.1"],
            set.Ranges.Select(r => r.ToString()).ToList());
        Assert.True(set.Contains(Ip("10.0.0.9")));
        Assert.False(set.Contains(Ip("10.0.0.10")));
        Assert.True(set.Contains(Ip("20.0.0.1")));
        Assert.False(set.Contains(Ip("20.0.0.2")));
    }

    [Fact]
    public void Mandatory_excludes_always_apply()
    {
        var set = TargetSet.Create([TargetParser.Parse("0.0.0.0/0")], []);
        Assert.False(set.Contains(Ip("127.0.0.1")));
        Assert.False(set.Contains(Ip("0.1.2.3")));
        Assert.False(set.Contains(Ip("224.0.0.1")));
        Assert.False(set.Contains(Ip("255.255.255.255")));
        Assert.False(set.Contains(Ip("169.254.1.1")));
        Assert.True(set.Contains(Ip("8.8.8.8")));
        Assert.Equal(4_294_967_296L - (2 * 16_777_216L) - 65_536 - 536_870_912, set.Count); // 2^32 - 0/8 - 127/8 - 169.254/16 - 224/3
    }

    [Fact]
    public void Indexing_walks_every_address_in_order()
    {
        var set = TargetSet.Create([TargetParser.Parse("1.0.0.0/30"), TargetParser.Parse("2.0.0.5-2.0.0.6")], []);
        var all = Enumerable.Range(0, (int)set.Count).Select(i => IpV4.Format(set[i])).ToList();
        Assert.Equal(["1.0.0.0", "1.0.0.1", "1.0.0.2", "1.0.0.3", "2.0.0.5", "2.0.0.6"], all);
        Assert.Throws<ArgumentOutOfRangeException>(() => set[set.Count]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(65537)]
    public void Permutation_is_a_bijection(int size)
    {
        var p = new Permutation(size, 12345);
        var seen = new bool[size];
        for (var i = 0; i < size; i++)
        {
            var v = p.Map(i);
            Assert.InRange(v, 0, size - 1);
            Assert.False(seen[v]);
            seen[v] = true;
        }
    }

    [Fact]
    public void Permutation_handles_internet_sized_ranges()
    {
        var size = 3_707_699_200L * 3;
        var p = new Permutation(size, 99);
        var values = new HashSet<long>();
        for (long i = 0; i < 100_000; i++)
        {
            var v = p.Map(i);
            Assert.InRange(v, 0, size - 1);
            Assert.True(values.Add(v));
        }

        // Shuffled, not sequential.
        Assert.NotEqual(1, p.Map(1) - p.Map(0));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("1.5m", 90)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("500ms", 0.5)]
    [InlineData("00:01:00", 60)]
    public void Durations(string text, double seconds)
    {
        Assert.True(CliParsing.TryParseDuration(text, out var value));
        Assert.Equal(seconds, value.TotalSeconds);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5s")]
    [InlineData("5x")]
    public void Invalid_durations(string text) => Assert.False(CliParsing.TryParseDuration(text, out _));

    [Fact]
    public void Ports()
    {
        Assert.True(CliParsing.TryParsePorts("25566,25565, 25570-25572,25565", out var ports));
        Assert.Equal([25565, 25566, 25570, 25571, 25572], ports.Select(p => (int)p));
        Assert.True(CliParsing.TryParsePorts("none", out var none));
        Assert.Empty(none);
        Assert.True(CliParsing.TryParsePorts("65535", out var max));
        Assert.Equal(65535, max.Single());
        Assert.True(CliParsing.TryParsePorts("65534-65535", out var top));
        Assert.Equal(2, top.Count);
        Assert.False(CliParsing.TryParsePorts("0", out _));
        Assert.False(CliParsing.TryParsePorts("70000", out _));
        Assert.False(CliParsing.TryParsePorts("10-5", out _));
        Assert.False(CliParsing.TryParsePorts("a", out _));
    }
}
