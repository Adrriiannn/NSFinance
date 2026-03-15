using System.Text.RegularExpressions;
using NSFinance.Api.Modules.ExpenseTracker.Models;

namespace NSFinance.Api.Modules.ExpenseTracker.Services;

public static class ExpensePlanPublicationModerationService
{
    private static readonly string[] BlockedTerms =
    [
        "hate speech",
        "racial slur",
        "loan shark",
        "money laundering",
        "terror funding",
        "ponzi",
        "get rich quick",
        "guaranteed profit",
        "guaranteed returns",
        "scam people",
        "abuse your family",
        "skip rent forever",
        "don't pay taxes",
        "evade tax"
    ];

    private static readonly string[] ReviewTerms =
    [
        "borrow to invest",
        "max out credit cards",
        "ignore bills",
        "skip insurance",
        "payday loan forever",
        "debt spiral",
        "hide spending",
        "casino strategy",
        "betting recovery",
        "flip debt fast"
    ];

    private static readonly Regex UrlRegex = new(@"https?://|www\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RepeatCharRegex = new(@"(.)\1{5,}", RegexOptions.Compiled);
    private static readonly Regex ShoutRegex = new(@"\b[A-Z]{8,}\b", RegexOptions.Compiled);

    public static ExpensePlanModerationScanResult Scan(
        string publicTitle,
        string? publicDescription,
        IReadOnlyList<string> tags)
    {
        var normalizedParts = new[]
        {
            publicTitle ?? string.Empty,
            publicDescription ?? string.Empty,
            string.Join(' ', tags)
        };

        var joined = string.Join('\n', normalizedParts)
            .Trim();
        var normalized = joined.ToLowerInvariant();

        var blockedMatches = BlockedTerms
            .Where(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (blockedMatches.Count > 0)
        {
            return new ExpensePlanModerationScanResult(
                ExpensePlanModerationStatuses.Blocked,
                true,
                false,
                $"Blocked by moderation rules: {string.Join(", ", blockedMatches)}.",
                blockedMatches);
        }

        var reviewMatches = ReviewTerms
            .Where(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (UrlRegex.IsMatch(joined))
        {
            reviewMatches.Add("external_links");
        }

        if (RepeatCharRegex.IsMatch(joined))
        {
            reviewMatches.Add("repeated_characters");
        }

        if (ShoutRegex.IsMatch(joined))
        {
            reviewMatches.Add("excessive_caps");
        }

        if (tags.Count > 0 && tags.GroupBy(tag => tag.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() >= 3))
        {
            reviewMatches.Add("repeated_tags");
        }

        if (reviewMatches.Count > 0)
        {
            return new ExpensePlanModerationScanResult(
                ExpensePlanModerationStatuses.NeedsReview,
                false,
                true,
                $"Needs moderation review: {string.Join(", ", reviewMatches.Distinct(StringComparer.OrdinalIgnoreCase))}.",
                reviewMatches.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        return new ExpensePlanModerationScanResult(
            ExpensePlanModerationStatuses.Approved,
            false,
            false,
            "Approved for publication.",
            []);
    }
}
