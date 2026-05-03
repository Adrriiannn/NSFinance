using System.Text.Json;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class JsonCompanionAmbiguityGuardCatalogueProvider(
    IChatTelemetry telemetry,
    ILogger<JsonCompanionAmbiguityGuardCatalogueProvider> logger,
    string? cataloguePath = null) : ICompanionAmbiguityGuardCatalogueProvider
{
    private const string RelativePath = "Modules/AI/Resources/places-ambiguity-guard-catalogue.json";
    private readonly Lazy<IReadOnlyList<CompanionAmbiguityGuardDefinition>> guards = new(() => LoadCatalogue(telemetry, logger, cataloguePath));

    public IReadOnlyList<CompanionAmbiguityGuardDefinition> GetAll()
    {
        return guards.Value;
    }

    private static IReadOnlyList<CompanionAmbiguityGuardDefinition> LoadCatalogue(
        IChatTelemetry telemetry,
        ILogger logger,
        string? cataloguePath)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(cataloguePath) ? ResolveCataloguePath() : cataloguePath;
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<GuardCatalogueDocument>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var parsed = document?.Guards?
                .Where(static guard => !string.IsNullOrWhiteSpace(guard.GuardId))
                .Select(static guard => new CompanionAmbiguityGuardDefinition(
                    NormalizeId(guard.GuardId),
                    NormalizeId(guard.Domain) is { Length: > 0 } domain ? domain : "general",
                    NormalizeConcepts(guard.RequestedConcepts),
                    NormalizeConcepts(guard.DangerousSiblingConcepts),
                    NormalizeConcepts(guard.CompatibleConcepts),
                    NormalizeFields(guard.EvidenceFields),
                    NormalizeAction(guard.DefaultAction),
                    guard.RequiresDetails,
                    guard.Confidence is > 0d and <= 1d ? guard.Confidence : 0.75d,
                    NormalizeExamples(guard.Examples),
                    guard.Notes))
                .Where(static guard => guard.RequestedConcepts.Count > 0 && guard.DangerousSiblingConcepts.Count > 0)
                .DistinctBy(static guard => guard.GuardId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];

            if (parsed.Length == 0)
            {
                throw new InvalidOperationException("Guard catalogue did not contain any valid guards.");
            }

            _ = telemetry.TrackAsync(
                "places.guard_catalogue.loaded",
                new Dictionary<string, object?>
                {
                    ["guardCount"] = parsed.Length,
                    ["schemaVersion"] = document?.SchemaVersion,
                    ["domainCount"] = parsed.Select(static guard => guard.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                },
                CancellationToken.None);

            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load Places ambiguity guard catalogue. Using emergency fallback catalogue.");
            _ = telemetry.TrackAsync(
                "places.guard_catalogue.load_failed",
                new Dictionary<string, object?>
                {
                    ["errorType"] = ex.GetType().Name,
                    ["fallbackGuardCount"] = EmergencyCatalogue.Count
                },
                CancellationToken.None);
            return EmergencyCatalogue;
        }
    }

    private static string ResolveCataloguePath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var currentDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        throw new FileNotFoundException("Places ambiguity guard catalogue was not found.", currentDirectoryPath);
    }

    private static IReadOnlyList<string> NormalizeConcepts(IReadOnlyList<string>? values)
    {
        return values?
            .SelectMany(static value => new[] { value, NormalizeConcept(value) })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static IReadOnlyList<string> NormalizeFields(IReadOnlyList<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? ["types", "primaryType"];
    }

    private static IReadOnlyList<string> NormalizeExamples(IReadOnlyList<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static string NormalizeAction(string? value)
    {
        return value is "hard_reject_if_confirmed"
            or "enrich_before_reject"
            or "soft_penalty_only"
            or "ranking_guard"
            or "identity_led_guard"
            ? value
            : "hard_reject_if_confirmed";
    }

    private static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().ToLowerInvariant().Select(static c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_');
    }

    internal static string NormalizeConcept(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly IReadOnlyList<CompanionAmbiguityGuardDefinition> EmergencyCatalogue =
    [
        Guard("bank_branch_vs_atm", "finance", ["bank branch", "bank"], ["atm", "cash machine"], ["bank", "financial institution"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("car_park_vs_public_park", "transport", ["car park", "parking"], ["park", "tourist attraction"], ["parking", "parking lot", "parking garage"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("post_office_vs_mailbox", "postal", ["post office"], ["mailbox", "post box", "parcel locker"], ["post office"], ["types", "primaryType"], "hard_reject_if_confirmed"),
        Guard("hotel_vs_hotel_restaurant", "accommodation", ["hotel", "lodging"], ["restaurant", "bar"], ["hotel", "lodging"], ["types", "primaryType"], "soft_penalty_only"),
        Guard("fine_dining_vs_fast_food", "food", ["fine dining", "upscale"], ["fast food", "takeaway", "cafe"], ["restaurant"], ["types", "primaryType", "priceLevel"], "ranking_guard"),
        Guard("restaurant_delivery_vs_dine_in_only", "food", ["delivery", "delivery restaurant"], ["dine in only", "takeaway not available"], ["restaurant", "meal delivery"], ["delivery", "takeout", "dineIn"], "enrich_before_reject", requiresDetails: true),
        Guard("parking_availability", "transport", ["parking", "free parking"], ["no parking"], ["parking", "nearby parking"], ["parkingOptions", "nearbyParking"], "enrich_before_reject", requiresDetails: true)
    ];

    private static CompanionAmbiguityGuardDefinition Guard(
        string guardId,
        string domain,
        IReadOnlyList<string> requested,
        IReadOnlyList<string> dangerous,
        IReadOnlyList<string> compatible,
        IReadOnlyList<string> fields,
        string action,
        bool requiresDetails = false)
    {
        return new CompanionAmbiguityGuardDefinition(guardId, domain, requested, dangerous, compatible, fields, action, requiresDetails, 0.9d, [], null);
    }

    private sealed record GuardCatalogueDocument(int SchemaVersion, IReadOnlyList<GuardCatalogueEntry>? Guards);

    private sealed record GuardCatalogueEntry(
        string GuardId,
        string Domain,
        IReadOnlyList<string>? RequestedConcepts,
        IReadOnlyList<string>? DangerousSiblingConcepts,
        IReadOnlyList<string>? CompatibleConcepts,
        IReadOnlyList<string>? EvidenceFields,
        string DefaultAction,
        bool RequiresDetails,
        double Confidence,
        IReadOnlyList<string>? Examples,
        string? Notes);
}
