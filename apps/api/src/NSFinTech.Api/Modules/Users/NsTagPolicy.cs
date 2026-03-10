using System.Text.RegularExpressions;

namespace NSFinTech.Api.Modules.Users;

public static class NsTagPolicy
{
    public const int MinLength = 2;
    public const int MaxLength = 12;

    private static readonly Regex AllowedPattern = new(
        @"^[a-z0-9_-]{2,12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var trimmed = rawValue.Trim();
        trimmed = trimmed.TrimStart('@');

        return trimmed.ToLowerInvariant();
    }

    public static bool IsValid(string normalizedTag) => AllowedPattern.IsMatch(normalizedTag);

    public static string ValidationMessage =>
        $"NS Tag can contain letters, numbers, '_' or '-', and must be {MinLength}-{MaxLength} characters.";
}
