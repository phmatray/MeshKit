using System.Text.RegularExpressions;

namespace MeshKit.Core.Definitions;

public static partial class Slug
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();

    /// <summary>Lowercase letters, digits and single dashes; never empty, never leading/trailing dash.</summary>
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);
}
