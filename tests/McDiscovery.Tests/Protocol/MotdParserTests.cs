using System.Text.Json;
using McDiscovery.Protocol.Text;

namespace McDiscovery.Tests.Protocol;

public class MotdParserTests
{
    [Fact]
    public void Legacy_codes_become_segments()
    {
        var result = MotdParser.FromLegacy("\u00a76Gold \u00a7lBold\u00a7r plain");

        Assert.Equal("Gold Bold plain", result.Plain);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal(new MotdSegment("Gold ", "#ffaa00"), result.Segments[0]);
        Assert.Equal(new MotdSegment("Bold", "#ffaa00", Bold: true), result.Segments[1]);
        Assert.Equal(new MotdSegment(" plain"), result.Segments[2]);
    }

    [Fact]
    public void Colour_code_resets_formatting()
    {
        var result = MotdParser.FromLegacy("\u00a7l\u00a7nX\u00a7cY");
        Assert.True(result.Segments[0].Bold);
        Assert.True(result.Segments[0].Underlined);
        Assert.Equal(new MotdSegment("Y", "#ff5555"), result.Segments[1]);
    }

    [Fact]
    public void Bukkit_hex_colour()
    {
        var result = MotdParser.FromLegacy("\u00a7x\u00a71\u00a72\u00a73\u00a7a\u00a7b\u00a7cHex");
        Assert.Equal(new MotdSegment("Hex", "#123abc"), Assert.Single(result.Segments));
    }

    [Fact]
    public void Json_component_with_extra_inherits_style()
    {
        using var doc = JsonDocument.Parse("""
            {"text":"A","color":"gold","bold":true,"extra":[{"text":"B","color":"#00FF00"},{"text":"C","bold":false},"D"]}
            """);
        var result = MotdParser.FromJson(doc.RootElement);

        Assert.Equal("ABCD", result.Plain);
        Assert.Equal(new MotdSegment("A", "#ffaa00", Bold: true), result.Segments[0]);
        Assert.Equal(new MotdSegment("B", "#00ff00", Bold: true), result.Segments[1]);
        Assert.Equal(new MotdSegment("C", "#ffaa00"), result.Segments[2]);
        Assert.Equal(new MotdSegment("D", "#ffaa00", Bold: true), result.Segments[3]);
    }

    [Fact]
    public void Json_array_first_element_is_parent()
    {
        using var doc = JsonDocument.Parse("""[{"text":"A","color":"red"},"B",{"text":"C","color":"blue"}]""");
        var result = MotdParser.FromJson(doc.RootElement);

        Assert.Equal("ABC", result.Plain);
        Assert.Equal("#ff5555", result.Segments[0].Color);
        Assert.Equal("AB", result.Segments[0].Text); // B inherits red and merges
        Assert.Equal("#5555ff", result.Segments[1].Color);
    }

    [Fact]
    public void Legacy_codes_inside_json_text()
    {
        using var doc = JsonDocument.Parse("\"\\u00a7aGreen\"");
        var result = MotdParser.FromJson(doc.RootElement);
        Assert.Equal(new MotdSegment("Green", "#55ff55"), Assert.Single(result.Segments));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("red;background:url(x)")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    public void Invalid_colours_are_dropped(string color)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { text = "x", color }));
        var result = MotdParser.FromJson(doc.RootElement);
        Assert.Null(Assert.Single(result.Segments).Color);
    }

    [Fact]
    public void Html_is_kept_as_text()
    {
        var result = MotdParser.FromLegacy("<script>alert(1)</script>");
        Assert.Equal("<script>alert(1)</script>", result.Plain); // rendered as text by the website, never as HTML
    }

    [Fact]
    public void Control_characters_are_removed_but_newlines_kept()
    {
        var result = MotdParser.FromLegacy("a\u0000b\u0007c\nd\u202Ee");
        Assert.Equal("abc\nde", result.Plain);
    }

    [Fact]
    public void Length_and_depth_are_capped()
    {
        var huge = MotdParser.FromLegacy(new string('x', 100_000));
        Assert.Equal(MotdParser.MaxTextLength, huge.Plain.Length);

        // 30 levels (JSON depth 60) parse fine but the component depth limit stops the recursion.
        var nested = string.Concat(Enumerable.Repeat("{\"text\":\"a\",\"extra\":[", 30)) + "\"b\"" + string.Concat(Enumerable.Repeat("]}", 30));
        var deep = MotdParser.FromJsonString(nested);
        Assert.InRange(deep.Plain.Length, 10, 29);
        Assert.DoesNotContain("b", deep.Plain, StringComparison.Ordinal);

        var many = MotdParser.FromLegacy(string.Concat(Enumerable.Range(0, 2000).Select(i => $"\u00a7{i % 10}x")));
        Assert.True(many.Segments.Count <= MotdParser.MaxSegments);
        Assert.Equal(2000, many.Plain.Length);
    }

    [Fact]
    public void FromJsonString_falls_back_to_legacy()
    {
        Assert.Equal("{not json", MotdParser.FromJsonString("{not json").Plain);
        Assert.Equal("kicked", MotdParser.FromJsonString("{\"text\":\"kicked\"}").Plain);
        Assert.Equal("plain", MotdParser.FromJsonString("plain").Plain);
    }

    [Fact]
    public void Segments_json_omits_defaults()
    {
        var json = MotdParser.FromLegacy("\u00a7lHi").SegmentsToJson();
        Assert.Equal("""[{"text":"Hi","bold":true}]""", json);
    }

    [Fact]
    public void Translate_and_unnamed_keys()
    {
        Assert.Equal("fallback", MotdParser.FromJsonString("""{"translate":"some.key","fallback":"fallback"}""").Plain);
        Assert.Equal("some.key", MotdParser.FromJsonString("""{"translate":"some.key"}""").Plain);
        Assert.Equal("nbt", MotdParser.FromJsonString("""{"":"nbt"}""").Plain);
    }
}
