using System.Text.RegularExpressions;

namespace zapret_gui;

public static class ZapretVersion
{
    private static readonly Regex VersionRegex = new(
        @"^v?(?<nums>\d+(?:\.\d+)*)(?<suffix>[a-zA-Z]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Compare(string? a, string? b)
    {
        var (partsA, suffixA) = Parse(a ?? "0");
        var (partsB, suffixB) = Parse(b ?? "0");

        var maxLen = Math.Max(partsA.Length, partsB.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var partA = i < partsA.Length ? partsA[i] : 0;
            var partB = i < partsB.Length ? partsB[i] : 0;
            if (partA != partB)
                return partA.CompareTo(partB);
        }

        return string.Compare(suffixA, suffixB, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewer(string latest, string current) =>
        Compare(latest, current) > 0;

    private static (int[] parts, string suffix) Parse(string version)
    {
        version = version.Trim();
        var match = VersionRegex.Match(version);
        if (!match.Success)
            return ([0], "");

        var parts = match.Groups["nums"].Value
            .Split('.')
            .Select(int.Parse)
            .ToArray();
        var suffix = match.Groups["suffix"].Value;
        return (parts, suffix);
    }
}
