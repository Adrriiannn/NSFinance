using System.Security.Cryptography;
using System.Text;
using NSFinance.Shared.Taxonomy;

namespace NSFinance.Api.Modules.Categories.Services;

public sealed record MerchantKnowledgeSeedEntry(
    string Pattern,
    string DisplayName,
    int DomainId,
    int CategoryId,
    int? SubcategoryId,
    string Direction);

public sealed record MerchantKnowledgeSeedConflict(
    string Pattern,
    int KeptCategoryId,
    int DroppedCategoryId);

// The catalog's merchant signals collapsed into one seed row per pattern
// (CAT-001). When the same signal serves an outflow and an inflow definition
// of the same taxonomy node (the savings-transfer pair), the merged row is
// direction "either" - dropping one side silently left savings arrivals
// uncategorized in production. Signals claimed by definitions with different
// nodes keep the first claim and report the conflict.
//
// A signal's boundary spaces are deliberate: " BAR " means the word BAR, not
// BARBER or BARNARDOS. Matching pads the statement text with one space at
// each end, so a padded pattern matches a whole word anywhere, including at
// the edges, and an unpadded pattern matches exactly as it always did.
public sealed class MerchantKnowledgeSeedPlan
{
    private static readonly Lazy<MerchantKnowledgeSeedPlan> CurrentPlan =
        new(() => Build(CategoryCharacteristicsCatalog.Definitions));

    private MerchantKnowledgeSeedPlan(
        IReadOnlyList<MerchantKnowledgeSeedEntry> entries,
        IReadOnlyList<MerchantKnowledgeSeedConflict> conflicts)
    {
        Entries = entries;
        Conflicts = conflicts;
        Hash = ComputeHash(entries);
    }

    public static MerchantKnowledgeSeedPlan Current => CurrentPlan.Value;

    public IReadOnlyList<MerchantKnowledgeSeedEntry> Entries { get; }

    public IReadOnlyList<MerchantKnowledgeSeedConflict> Conflicts { get; }

    // Order-independent SHA-256 over every entry's pattern, node, and
    // direction. Pinned per catalog version by test, and recorded on each
    // seed run, so a signal change without a version bump cannot ship.
    public string Hash { get; }

    public static MerchantKnowledgeSeedPlan Build(IEnumerable<CategoryCharacteristicsDefinition> definitions)
    {
        var plan = new Dictionary<string, MerchantKnowledgeSeedEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        var conflicts = new List<MerchantKnowledgeSeedConflict>();

        foreach (var definition in definitions)
        {
            if (!CharacteristicsTaxonomyResolver.TryResolve(
                    definition,
                    out var domainId,
                    out var categoryId,
                    out var subcategoryId))
            {
                continue;
            }

            var direction = definition.DirectionExpectation switch
            {
                CharacteristicsDirection.Outflow => "outflow",
                CharacteristicsDirection.Inflow => "inflow",
                _ => "either"
            };

            foreach (var signal in definition.MerchantSignals)
            {
                var displayName = signal.Trim();
                if (displayName.Length < 2)
                {
                    continue;
                }

                var pattern = NormalizePattern(signal);
                if (!plan.TryGetValue(pattern, out var existing))
                {
                    plan.Add(pattern, new MerchantKnowledgeSeedEntry(
                        pattern,
                        displayName,
                        domainId,
                        categoryId,
                        subcategoryId,
                        direction));
                    order.Add(pattern);
                    continue;
                }

                if (existing.DomainId == domainId
                    && existing.CategoryId == categoryId
                    && existing.SubcategoryId == subcategoryId)
                {
                    if (existing.Direction != direction)
                    {
                        plan[pattern] = existing with { Direction = "either" };
                    }

                    continue;
                }

                conflicts.Add(new MerchantKnowledgeSeedConflict(pattern, existing.CategoryId, categoryId));
            }
        }

        return new MerchantKnowledgeSeedPlan(order.Select(p => plan[p]).ToList(), conflicts);
    }

    // Uppercase, with any boundary whitespace collapsed to exactly one space
    // so " BAR" and "BAR  " stay word boundaries rather than typos.
    public static string NormalizePattern(string signal)
    {
        var trimmed = signal.Trim().ToUpperInvariant();
        var leading = signal.Length > 0 && char.IsWhiteSpace(signal[0]) ? " " : string.Empty;
        var trailing = signal.Length > 0 && char.IsWhiteSpace(signal[^1]) ? " " : string.Empty;
        return leading + trimmed + trailing;
    }

    // The statement text a pattern is matched against: normalized, then
    // padded so boundary patterns match whole words at the edges too.
    public static string MatchHaystack(string rawDescription)
    {
        return " " + DeterministicMerchantCategorizer.NormalizeStatementText(rawDescription) + " ";
    }

    private static string ComputeHash(IReadOnlyList<MerchantKnowledgeSeedEntry> entries)
    {
        var canonical = new StringBuilder();
        foreach (var entry in entries.OrderBy(e => e.Pattern, StringComparer.Ordinal))
        {
            canonical
                .Append(entry.Pattern).Append('\t')
                .Append(entry.DomainId).Append('\t')
                .Append(entry.CategoryId).Append('\t')
                .Append(entry.SubcategoryId?.ToString() ?? "-").Append('\t')
                .Append(entry.Direction).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
