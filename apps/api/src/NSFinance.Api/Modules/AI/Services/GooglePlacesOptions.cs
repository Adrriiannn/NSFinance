using Microsoft.Extensions.Options;

namespace NSFinance.Api.Modules.AI.Services;

public sealed class GooglePlacesOptions
{
    public const string SectionName = "CompanionAI:Places";

    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://places.googleapis.com";
    public string PlacesPhotoPublicBaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxCompanionCandidates { get; set; } = 8;
    public int MaxMerchantLookupCandidates { get; set; } = 5;
    public int CompanionCacheTtlSeconds { get; set; } = 600;
    public int MerchantLookupCacheTtlSeconds { get; set; } = 900;
    public int PlaceDetailsCacheTtlSeconds { get; set; } = 900;
    public int FailureCacheTtlSeconds { get; set; } = 60;
    public string DefaultLanguageCode { get; set; } = "en";
    public string DefaultRegionCode { get; set; } = string.Empty;
    public int DefaultSearchRadiusMeters { get; set; } = 5000;
}

public sealed class GooglePlacesOptionsValidator : IValidateOptions<GooglePlacesOptions>
{
    public ValidateOptionsResult Validate(string? name, GooglePlacesOptions options)
    {
        var failures = new List<string>();
        if (options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "CompanionAI:Places:ApiKey is required when CompanionAI:Places:Enabled is true.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add("CompanionAI:Places:BaseUrl must be an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.PlacesPhotoPublicBaseUrl)
            && !Uri.TryCreate(options.PlacesPhotoPublicBaseUrl, UriKind.Absolute, out _))
        {
            failures.Add("CompanionAI:Places:PlacesPhotoPublicBaseUrl must be an absolute URL when set.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add("CompanionAI:Places:TimeoutSeconds must be > 0.");
        }

        if (options.MaxCompanionCandidates <= 0)
        {
            failures.Add("CompanionAI:Places:MaxCompanionCandidates must be > 0.");
        }

        if (options.MaxMerchantLookupCandidates <= 0)
        {
            failures.Add("CompanionAI:Places:MaxMerchantLookupCandidates must be > 0.");
        }

        if (options.CompanionCacheTtlSeconds <= 0)
        {
            failures.Add("CompanionAI:Places:CompanionCacheTtlSeconds must be > 0.");
        }

        if (options.MerchantLookupCacheTtlSeconds <= 0)
        {
            failures.Add("CompanionAI:Places:MerchantLookupCacheTtlSeconds must be > 0.");
        }

        if (options.PlaceDetailsCacheTtlSeconds <= 0)
        {
            failures.Add("CompanionAI:Places:PlaceDetailsCacheTtlSeconds must be > 0.");
        }

        if (options.FailureCacheTtlSeconds <= 0)
        {
            failures.Add("CompanionAI:Places:FailureCacheTtlSeconds must be > 0.");
        }

        if (options.DefaultSearchRadiusMeters <= 0)
        {
            failures.Add("CompanionAI:Places:DefaultSearchRadiusMeters must be > 0.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
