using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSFinance.Api.Modules.Accounts.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;
using Npgsql;
using System.Text.Json;

namespace NSFinance.Api.Modules.Accounts.Services;

public sealed class AccountService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    AccountBalanceReadService accountBalanceReadService,
    ILogger<AccountService> logger)
{
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await QueryAccountsWithBranding()
                .ToListAsync(cancellationToken);
            var enriched = await TryEnrichMissingBrandingFromLinkedAccountPayloadAsync(accounts, cancellationToken);
            var normalized = await NormalizeAccountDisplayNamesAsync(enriched, cancellationToken);
            return await accountBalanceReadService.AttachBalancesAsync(normalized, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            logger.LogWarning(
                exception,
                "Provider branding columns are missing in the current database schema. Falling back to accounts without provider branding metadata.");

            var accounts = await QueryAccountsWithoutBranding()
                .ToListAsync(cancellationToken);
            var enriched = await TryEnrichMissingBrandingFromLinkedAccountPayloadAsync(accounts, cancellationToken);
            var normalized = await NormalizeAccountDisplayNamesAsync(enriched, cancellationToken);
            return await accountBalanceReadService.AttachBalancesAsync(normalized, cancellationToken);
        }
    }

    public async Task<AccountDto?> GetAccountByIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            var account = await QueryAccountByIdWithBranding(accountId)
                .SingleOrDefaultAsync(cancellationToken);

            if (account is null)
            {
                return null;
            }

            var enriched = await TryEnrichMissingBrandingFromLinkedAccountPayloadAsync([account], cancellationToken);
            var normalized = await NormalizeAccountDisplayNamesAsync(enriched, cancellationToken);
            var withBalances = await accountBalanceReadService.AttachBalancesAsync(normalized, cancellationToken);
            return withBalances.FirstOrDefault();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            logger.LogWarning(
                exception,
                "Provider branding columns are missing in the current database schema. Falling back to account details without provider branding metadata.");

            var account = await QueryAccountByIdWithoutBranding(accountId)
                .SingleOrDefaultAsync(cancellationToken);

            if (account is null)
            {
                return null;
            }

            var enriched = await TryEnrichMissingBrandingFromLinkedAccountPayloadAsync([account], cancellationToken);
            var normalized = await NormalizeAccountDisplayNamesAsync(enriched, cancellationToken);
            var withBalances = await accountBalanceReadService.AttachBalancesAsync(normalized, cancellationToken);
            return withBalances.FirstOrDefault();
        }
    }

    private async Task<IReadOnlyList<AccountDto>> NormalizeAccountDisplayNamesAsync(
        IReadOnlyList<AccountDto> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0)
        {
            return accounts;
        }

        var accountIds = accounts.Select(account => account.Id).ToArray();
        var linkedRows = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(linked =>
                linked.FinancialAccountId.HasValue
                && accountIds.Contains(linked.FinancialAccountId.Value))
            .OrderByDescending(linked => linked.UpdatedUtc)
            .Select(linked => new
            {
                FinancialAccountId = linked.FinancialAccountId!.Value,
                linked.DisplayName,
                ProviderDisplayName = linked.Connection != null ? linked.Connection.ProviderDisplayName : null,
                ProviderId = linked.Connection != null ? linked.Connection.ProviderId : null,
                linked.AccountType,
                linked.Currency,
                linked.AccountNumberMetadataJson,
                ConnectedFullName = linked.Connection != null && linked.Connection.IdentityInfo != null
                    ? linked.Connection.IdentityInfo.FullName
                    : null
            })
            .ToListAsync(cancellationToken);

        if (linkedRows.Count == 0)
        {
            return accounts;
        }

        var linkedByAccount = new Dictionary<Guid, LinkedAccountNameProjection>();
        foreach (var row in linkedRows)
        {
            if (linkedByAccount.ContainsKey(row.FinancialAccountId))
            {
                continue;
            }

            var resolvedName = ResolveLinkedAccountDisplayName(
                row.DisplayName,
                row.ProviderDisplayName,
                row.ProviderId,
                row.AccountType,
                row.Currency,
                row.ConnectedFullName,
                row.AccountNumberMetadataJson);

            linkedByAccount[row.FinancialAccountId] = new LinkedAccountNameProjection(
                resolvedName,
                row.ConnectedFullName);
        }

        return accounts
            .Select(account =>
            {
                if (!linkedByAccount.TryGetValue(account.Id, out var linkedProjection))
                {
                    return account;
                }

                if (!ShouldReplaceAccountName(
                    account.Name,
                    linkedProjection.ResolvedName,
                    linkedProjection.ConnectedFullName))
                {
                    return account;
                }

                return account with { Name = linkedProjection.ResolvedName };
            })
            .ToList();
    }

    private static bool ShouldReplaceAccountName(
        string currentName,
        string proposedName,
        string? connectedFullName)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentName))
        {
            return true;
        }

        if (string.Equals(currentName.Trim(), proposedName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return LooksLikeConnectedIdentity(currentName.Trim(), connectedFullName);
    }

    private static string ResolveLinkedAccountDisplayName(
        string? displayName,
        string? providerDisplayName,
        string? providerId,
        string? accountType,
        string currency,
        string? connectedFullName,
        string? accountNumberMetadataJson)
    {
        var normalizedDisplayName = NormalizeLabel(displayName);
        if (!string.IsNullOrWhiteSpace(normalizedDisplayName)
            && LooksLikeConnectedIdentity(normalizedDisplayName, connectedFullName))
        {
            normalizedDisplayName = null;
        }

        var providerLabel = ResolveProviderDisplayLabel(providerDisplayName)
            ?? ResolveProviderDisplayLabel(providerId)
            ?? ResolveProviderDisplayLabel(normalizedDisplayName);
        var maskedHint = ExtractMaskedAccountHint(accountNumberMetadataJson);
        if (!string.IsNullOrWhiteSpace(providerLabel))
        {
            if (!string.IsNullOrWhiteSpace(maskedHint))
            {
                return $"{providerLabel} **{maskedHint}";
            }

            return providerLabel;
        }

        if (!string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return normalizedDisplayName;
        }

        var resolvedCurrency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
        var friendlyType = ResolveFriendlyAccountType(accountType);
        if (!string.IsNullOrWhiteSpace(maskedHint))
        {
            return $"{resolvedCurrency} {friendlyType} **{maskedHint}";
        }

        return $"{resolvedCurrency} {friendlyType}";
    }

    private static string? ResolveProviderDisplayLabel(string? providerDisplayName)
    {
        var normalized = NormalizeLabel(providerDisplayName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var compact = normalized;
        if (compact.StartsWith("ob-", StringComparison.OrdinalIgnoreCase)
            || compact.StartsWith("ob_", StringComparison.OrdinalIgnoreCase)
            || compact.StartsWith("ob ", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[3..];
        }

        var tokens = compact
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (tokens.Count > 1)
        {
            var lastToken = tokens[^1];
            if (lastToken.Equals("ie", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("uk", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("gb", StringComparison.OrdinalIgnoreCase)
                || lastToken.Equals("eu", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(tokens.Count - 1);
            }
        }

        if (tokens.Count == 0)
        {
            return normalized;
        }

        var joinedSingle = string.Join("", tokens).ToUpperInvariant();
        if (joinedSingle is "AIB" or "BOI" or "PTSB" or "TSB" or "HSBC" or "MBNA" or "RBS")
        {
            return joinedSingle;
        }

        return string.Join(" ", tokens.Select(ToProviderTitleCase));
    }

    private static string ToProviderTitleCase(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (token.Length == 1)
        {
            return token.ToUpperInvariant();
        }

        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static string? NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool LooksLikeConnectedIdentity(string accountLabel, string? connectedFullName)
    {
        var normalizedConnectedName = NormalizeLabel(connectedFullName);
        if (normalizedConnectedName is null)
        {
            return false;
        }

        var accountTokens = accountLabel
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .OrderBy(token => token)
            .ToArray();

        var connectedTokens = normalizedConnectedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .OrderBy(token => token)
            .ToArray();

        if (accountTokens.Length < 2 || accountTokens.Length != connectedTokens.Length)
        {
            return false;
        }

        return accountTokens.SequenceEqual(connectedTokens);
    }

    private static string ResolveFriendlyAccountType(string? accountType)
    {
        var normalized = accountType?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "transaction" => "current account",
            "current" => "current account",
            "checking" => "current account",
            "savings" => "savings account",
            "credit" => "credit account",
            "loan" => "loan account",
            _ => "account"
        };
    }

    private static string? ExtractMaskedAccountHint(string? accountNumberMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(accountNumberMetadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(accountNumberMetadataJson);
            var root = document.RootElement;
            string?[] directCandidates =
            [
                TryGetJsonString(root, "iban"),
                TryGetJsonString(root, "number"),
                TryGetJsonString(root, "pan"),
                TryGetJsonString(root, "masked_pan")
            ];

            foreach (var candidate in directCandidates)
            {
                var normalized = ExtractMaskedHintFromValue(candidate);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            if (TryGetJsonProperty(root, "account_number", out var accountNumberNode))
            {
                var fromAccountNumber = ExtractMaskedHintFromValue(TryGetJsonString(accountNumberNode, "number"));
                if (!string.IsNullOrWhiteSpace(fromAccountNumber))
                {
                    return fromAccountNumber;
                }
            }

            if (TryGetJsonProperty(root, "sort_code_account_number", out var sortCodeNode))
            {
                var fromSortCode = ExtractMaskedHintFromValue(TryGetJsonString(sortCodeNode, "account_number"));
                if (!string.IsNullOrWhiteSpace(fromSortCode))
                {
                    return fromSortCode;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out propertyValue))
        {
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static string? TryGetJsonString(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.ToString();
        }

        return null;
    }

    private static string? ExtractMaskedHintFromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var alphanumeric = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (alphanumeric.Length < 4)
        {
            return null;
        }

        return alphanumeric[^4..].ToUpperInvariant();
    }

    private sealed record LinkedAccountNameProjection(
        string ResolvedName,
        string? ConnectedFullName);
    private async Task<IReadOnlyList<AccountDto>> TryEnrichMissingBrandingFromLinkedAccountPayloadAsync(
        IReadOnlyList<AccountDto> accounts,
        CancellationToken cancellationToken)
    {
        try
        {
            return await EnrichMissingBrandingFromLinkedAccountPayloadAsync(accounts, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Provider branding enrichment failed. Returning base account payload without raw-payload branding fallback.");
            return accounts;
        }
    }

    private async Task<IReadOnlyList<AccountDto>> EnrichMissingBrandingFromLinkedAccountPayloadAsync(
        IReadOnlyList<AccountDto> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0)
        {
            return accounts;
        }

        var accountIdsNeedingFallback = accounts
            .Where(NeedsBrandingFallback)
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        if (accountIdsNeedingFallback.Length == 0)
        {
            return accounts;
        }

        var linkedPayloadRows = await dbContext.LinkedBankAccounts
            .AsNoTracking()
            .Where(x =>
                x.FinancialAccountId.HasValue
                && accountIdsNeedingFallback.Contains(x.FinancialAccountId.Value))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new
            {
                FinancialAccountId = x.FinancialAccountId!.Value,
                x.RawPayloadJson
            })
            .ToListAsync(cancellationToken);

        if (linkedPayloadRows.Count == 0)
        {
            return accounts;
        }

        var fallbackByAccountId = new Dictionary<Guid, ProviderBrandingFallback>();
        foreach (var row in linkedPayloadRows)
        {
            if (fallbackByAccountId.ContainsKey(row.FinancialAccountId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.RawPayloadJson))
            {
                continue;
            }

            var fallback = ParseProviderBrandingFallback(row.RawPayloadJson);
            if (fallback is null)
            {
                continue;
            }

            fallbackByAccountId[row.FinancialAccountId] = fallback;
        }

        if (fallbackByAccountId.Count == 0)
        {
            return accounts;
        }

        return accounts
            .Select(account =>
            {
                if (!fallbackByAccountId.TryGetValue(account.Id, out var fallback))
                {
                    return account;
                }

                var providerId = CoalesceNonEmpty(account.ProviderId, fallback.ProviderId);
                var providerDisplayName = CoalesceNonEmpty(account.ProviderDisplayName, fallback.ProviderDisplayName);
                var providerIconUrl = CoalesceNonEmpty(account.ProviderIconUrl, fallback.ProviderIconUrl);
                var providerLogoUrl = CoalesceNonEmpty(account.ProviderLogoUrl, fallback.ProviderLogoUrl);
                var providerBrandBgColor = CoalesceNonEmpty(account.ProviderBrandBgColor, fallback.ProviderBrandBgColor);
                var hasProviderBranding =
                    !string.IsNullOrWhiteSpace(providerIconUrl)
                    || !string.IsNullOrWhiteSpace(providerLogoUrl)
                    || !string.IsNullOrWhiteSpace(providerDisplayName);

                return account with
                {
                    ProviderId = providerId,
                    ProviderDisplayName = providerDisplayName,
                    ProviderIconUrl = providerIconUrl,
                    ProviderLogoUrl = providerLogoUrl,
                    ProviderBrandBgColor = providerBrandBgColor,
                    HasProviderBranding = hasProviderBranding
                };
            })
            .ToList();
    }

    private static bool NeedsBrandingFallback(AccountDto account)
    {
        var hasProviderVisualAsset =
            !string.IsNullOrWhiteSpace(account.ProviderIconUrl)
            || !string.IsNullOrWhiteSpace(account.ProviderLogoUrl);

        return !hasProviderVisualAsset;
    }

    private static ProviderBrandingFallback? ParseProviderBrandingFallback(string? rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayloadJson);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var providerNode = root;
            if (root.TryGetProperty("provider", out var providerElement) && providerElement.ValueKind == JsonValueKind.Object)
            {
                providerNode = providerElement;
            }

            return new ProviderBrandingFallback(
                ProviderId: ReadJsonString(providerNode, "provider_id"),
                ProviderDisplayName: ReadJsonString(providerNode, "display_name"),
                ProviderIconUrl: ReadJsonString(providerNode, "icon_uri"),
                ProviderLogoUrl: ReadJsonString(providerNode, "logo_uri"),
                ProviderBrandBgColor: ReadJsonString(providerNode, "bg_color"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var value = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? CoalesceNonEmpty(string? primary, string? fallback)
    {
        return !string.IsNullOrWhiteSpace(primary) ? primary : fallback;
    }

    private sealed record ProviderBrandingFallback(
        string? ProviderId,
        string? ProviderDisplayName,
        string? ProviderIconUrl,
        string? ProviderLogoUrl,
        string? ProviderBrandBgColor);

    private IQueryable<AccountDto> QueryAccountsWithBranding()
    {
        return dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.Source,
                CurrentBalance = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Available ?? balance.Current))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                TransactionCount = x.Transactions.Count,
                x.CreatedUtc,
                Provider = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id && linked.Connection != null)
                    .OrderByDescending(linked => linked.UpdatedUtc)
                    .Select(linked => new
                    {
                        linked.Connection!.ProviderId,
                        linked.Connection.ProviderDisplayName,
                        ProviderIconUrl = linked.Connection.ProviderIconUri,
                        ProviderLogoUrl = linked.Connection.ProviderLogoUri,
                        linked.Connection.ProviderBrandBgColor
                    })
                    .FirstOrDefault()
            })
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.CurrentBalance,
                x.TransactionCount,
                x.CreatedUtc,
                x.Provider != null ? x.Provider.ProviderId : null,
                x.Provider != null ? x.Provider.ProviderDisplayName : null,
                x.Provider != null ? x.Provider.ProviderIconUrl : null,
                x.Provider != null ? x.Provider.ProviderLogoUrl : null,
                x.Provider != null ? x.Provider.ProviderBrandBgColor : null,
                x.Provider != null
                    && (x.Provider.ProviderIconUrl != null
                        || x.Provider.ProviderLogoUrl != null
                        || x.Provider.ProviderDisplayName != null),
                null,
                x.Source));
    }

    private IQueryable<AccountDto> QueryAccountsWithoutBranding()
    {
        return dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.UserId == currentUserProvider.UserId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Available ?? balance.Current))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                x.Transactions.Count,
                x.CreatedUtc,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                x.Source));
    }

    private IQueryable<AccountDto> QueryAccountByIdWithBranding(Guid accountId)
    {
        return dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.UserId == currentUserProvider.UserId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.Source,
                CurrentBalance = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Available ?? balance.Current))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                TransactionCount = x.Transactions.Count,
                x.CreatedUtc,
                Provider = dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id && linked.Connection != null)
                    .OrderByDescending(linked => linked.UpdatedUtc)
                    .Select(linked => new
                    {
                        linked.Connection!.ProviderId,
                        linked.Connection.ProviderDisplayName,
                        ProviderIconUrl = linked.Connection.ProviderIconUri,
                        ProviderLogoUrl = linked.Connection.ProviderLogoUri,
                        linked.Connection.ProviderBrandBgColor
                    })
                    .FirstOrDefault()
            })
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                x.CurrentBalance,
                x.TransactionCount,
                x.CreatedUtc,
                x.Provider != null ? x.Provider.ProviderId : null,
                x.Provider != null ? x.Provider.ProviderDisplayName : null,
                x.Provider != null ? x.Provider.ProviderIconUrl : null,
                x.Provider != null ? x.Provider.ProviderLogoUrl : null,
                x.Provider != null ? x.Provider.ProviderBrandBgColor : null,
                x.Provider != null
                    && (x.Provider.ProviderIconUrl != null
                        || x.Provider.ProviderLogoUrl != null
                        || x.Provider.ProviderDisplayName != null),
                null,
                x.Source));
    }

    private IQueryable<AccountDto> QueryAccountByIdWithoutBranding(Guid accountId)
    {
        return dbContext.FinancialAccounts
            .AsNoTracking()
            .Where(x => x.Id == accountId && x.UserId == currentUserProvider.UserId)
            .Select(x => new AccountDto(
                x.Id,
                x.Name,
                x.Type,
                x.Currency,
                dbContext.LinkedBankAccounts
                    .Where(linked => linked.FinancialAccountId == x.Id)
                    .Select(linked => dbContext.BankBalanceSnapshots
                        .Where(balance => balance.LinkedBankAccountId == linked.Id)
                        .OrderByDescending(balance => balance.CapturedUtc)
                        .Select(balance => (decimal?)(balance.Available ?? balance.Current))
                        .FirstOrDefault())
                    .FirstOrDefault() ?? (x.Transactions.Select(t => (decimal?)t.Amount).Sum() ?? 0m),
                x.Transactions.Count,
                x.CreatedUtc,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                x.Source));
    }

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        var account = new FinancialAccount
        {
            Id = Guid.NewGuid(),
            UserId = currentUserProvider.UserId,
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Source = FinancialAccountSources.Manual,
            CreatedUtc = utcNow
        };

        dbContext.FinancialAccounts.Add(account);

        var openingBalance = request.OpeningBalance.GetValueOrDefault();
        if (openingBalance != 0)
        {
            dbContext.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                FinancialAccountId = account.Id,
                Amount = openingBalance,
                Currency = account.Currency,
                Description = "Opening balance",
                EntryKind = TransactionEntryKinds.OpeningBalanceAdjustment,
                AnalyticsTreatment = TransactionAnalyticsTreatments.BalanceOnly,
                BookedAtUtc = utcNow,
                DeterministicClassificationStatus = DeterministicClassificationStatus.EvaluatedNoMatchingRule,
                DeterministicClassificationRuleKey = "provenance.opening_balance",
                DeterministicReasonCode = "balance_only_entry",
                DeterministicClassificationEvaluatedUtc = utcNow,
                DeterministicClassificationTerminal = true,
                CreatedUtc = utcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await AttachBalanceAsync(new AccountDto(
            account.Id,
            account.Name,
            account.Type,
            account.Currency,
            openingBalance,
            openingBalance == 0 ? 0 : 1,
            account.CreatedUtc,
            null,
            null,
            null,
            null,
            null,
            false,
            Source: account.Source), cancellationToken);
    }

    public async Task<AccountDto?> UpdateAccountAsync(
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.FinancialAccounts
            .SingleOrDefaultAsync(
                x => x.Id == accountId && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (account is null)
        {
            return null;
        }

        account.Name = request.Name.Trim();
        account.Type = request.Type.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        var currentBalance = await dbContext.Transactions
            .Where(x => x.FinancialAccountId == account.Id)
            .Select(x => (decimal?)x.Amount)
            .SumAsync(cancellationToken) ?? 0m;

        var transactionCount = await dbContext.Transactions
            .CountAsync(x => x.FinancialAccountId == account.Id, cancellationToken);

        return await AttachBalanceAsync(new AccountDto(
            account.Id,
            account.Name,
            account.Type,
            account.Currency,
            currentBalance,
            transactionCount,
            account.CreatedUtc,
            null,
            null,
            null,
            null,
            null,
            false,
            Source: account.Source), cancellationToken);
    }

    private async Task<AccountDto> AttachBalanceAsync(
        AccountDto account,
        CancellationToken cancellationToken)
    {
        var accounts = await accountBalanceReadService.AttachBalancesAsync([account], cancellationToken);
        return accounts[0];
    }

    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.FinancialAccounts
            .SingleOrDefaultAsync(
                x => x.Id == accountId && x.UserId == currentUserProvider.UserId,
                cancellationToken);

        if (account is null)
        {
            return false;
        }

        dbContext.FinancialAccounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
