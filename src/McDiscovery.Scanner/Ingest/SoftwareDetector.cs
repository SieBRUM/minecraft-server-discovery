namespace McDiscovery.Scanner.Ingest;

/// <summary>Best-effort guess of the server software from the version string and query data.</summary>
public static class SoftwareDetector
{
    // Order matters: forks before the projects they are based on (Purpur before Paper before Spigot ...).
    private static readonly (string Keyword, string Name)[] Known =
    [
        ("Folia", "Folia"),
        ("Purpur", "Purpur"),
        ("Pufferfish", "Pufferfish"),
        ("Leaves", "Leaves"),
        ("Leaf", "Leaf"),
        ("Mohist", "Mohist"),
        ("Arclight", "Arclight"),
        ("Magma", "Magma"),
        ("CatServer", "CatServer"),
        ("Banner", "Banner"),
        ("Youer", "Youer"),
        ("Paper", "Paper"),
        ("Spigot", "Spigot"),
        ("CraftBukkit", "CraftBukkit"),
        ("Bukkit", "Bukkit"),
        ("Velocity", "Velocity"),
        ("Waterfall", "Waterfall"),
        ("FlameCord", "FlameCord"),
        ("Travertine", "Travertine"),
        ("BungeeCord", "BungeeCord"),
        ("Bungee", "BungeeCord"),
        ("Gate", "Gate"),
        ("NeoForge", "NeoForge"),
        ("Forge", "Forge"),
        ("Fabric", "Fabric"),
        ("Quilt", "Quilt"),
        ("Sponge", "Sponge"),
        ("Glowstone", "Glowstone"),
        ("Cuberite", "Cuberite"),
        ("Minestom", "Minestom"),
        ("NanoLimbo", "NanoLimbo"),
        ("LimboAPI", "Limbo"),
        ("Geyser", "Geyser"),
        ("Nukkit", "Nukkit"),
        ("PowerNukkit", "PowerNukkit"),
        ("PocketMine", "PocketMine-MP"),
        ("TCPShield", "TCPShield"),
    ];

    public static string? Detect(params string?[] sources)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            foreach (var (keyword, name) in Known)
            {
                if (ContainsWord(source, keyword))
                {
                    return name;
                }
            }
        }

        return null;
    }

    /// <summary>Case-insensitive match that doesn't hit inside other words ("Leaf" in "Leaflet").</summary>
    private static bool ContainsWord(string text, string word)
    {
        var start = 0;
        while (true)
        {
            var i = text.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return false;
            }

            var before = i == 0 || !char.IsLetter(text[i - 1]);
            var end = i + word.Length;
            var after = end >= text.Length || !char.IsLower(text[end]);
            if (before && after)
            {
                return true;
            }

            start = i + 1;
        }
    }
}
