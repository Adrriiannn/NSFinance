using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Common.Contracts;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Modules.Users.Services;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class FinancialCommitmentReadService(
    AppDbContext dbContext,
    ICurrentUserProvider currentUserProvider,
    TimeProvider timeProvider,
    InferredFinancialCommitmentService inferredCommitmentService,
    FinancialCommitmentMergePolicy mergePolicy,
    UserFinancialCommitmentService userCommitmentService)
{
    internal const int DefaultLimit = 100;
    internal const int MaximumLimit = 200;
    private static readonly TimeSpan FutureTimestampTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InferredLookbackWindow = TimeSpan.FromDays(800);
    private const int InferredTransactionLimit = 500;

    public async Task<ServiceResult<FinancialCommitmentsDto>> ListAsync(
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        return await ListAsync(requestedLimit, false, cancellationToken);
    }

    public async Task<ServiceResult<FinancialCommitmentsDto>> ListAsync(
        int? requestedLimit,
        bool includeDismissed,
        CancellationToken cancellationToken)
    {
        var limit = requestedLimit ?? DefaultLimit;
        if (limit is < 1 or > MaximumLimit)
        {
            return ServiceResult<FinancialCommitmentsDto>.Fail(
                $"Limit must be between 1 and {MaximumLimit}.",
                "commitment_limit_invalid",
                StatusCodes.Status400BadRequest);
        }

        var userId = currentUserProvider.UserId;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var baseItems = await BuildBaseItemsAsync(userId, limit + 1, utcNow, cancellationToken);
        var effectiveItems = await userCommitmentService.ApplyAsync(
            baseItems,
            includeDismissed,
            cancellationToken);
        var items = effectiveItems
            .OrderBy(item => item.NextDateUtc is null)
            .ThenBy(item => item.NextDateUtc)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var isTruncated = items.Count > limit;
        if (isTruncated)
        {
            items = items.Take(limit).ToList();
        }

        return ServiceResult<FinancialCommitmentsDto>.Ok(
            new FinancialCommitmentsDto(utcNow, limit, isTruncated, items));
    }

    public async Task<FinancialCommitmentDto?> FindBaseAsync(
        string commitmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commitmentId) || commitmentId.Length > 200)
        {
            return null;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var items = await BuildBaseItemsAsync(
            currentUserProvider.UserId,
            MaximumLimit + 1,
            utcNow,
            cancellationToken);
        return items.FirstOrDefault(item => string.Equals(item.Id, commitmentId, StringComparison.Ordinal));
    }

    public async Task<FinancialCommitmentDto?> FindAsync(
        string commitmentId,
        bool includeDismissed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commitmentId) || commitmentId.Length > 200)
        {
            return null;
        }

        var baseItem = commitmentId.StartsWith("user_manual:", StringComparison.Ordinal)
            ? null
            : await FindBaseAsync(commitmentId, cancellationToken);
        var effectiveItems = await userCommitmentService.ApplyAsync(
            baseItem is null ? [] : [baseItem],
            includeDismissed,
            cancellationToken);
        return effectiveItems.FirstOrDefault(item =>
            string.Equals(item.Id, commitmentId, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<FinancialCommitmentDto>> BuildBaseItemsAsync(
        Guid userId,
        int providerLimit,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var directDebits = await BuildDirectDebitQuery(userId, providerLimit)
            .ToListAsync(cancellationToken);
        var standingOrders = await BuildStandingOrderQuery(userId, providerLimit)
            .ToListAsync(cancellationToken);
        var inferredRows = await BuildInferredTransactionQuery(userId, utcNow, InferredTransactionLimit)
            .ToListAsync(cancellationToken);
        var inferredCommitments = await inferredCommitmentService.BuildAsync(
            inferredRows,
            utcNow,
            cancellationToken);
        var providerCommitments = directDebits
            .Select(row => FinancialCommitmentProviderMapper.MapDirectDebit(row, utcNow))
            .Concat(standingOrders.Select(row => FinancialCommitmentProviderMapper.MapStandingOrder(row, utcNow)))
            .ToList();
        return mergePolicy.Merge(providerCommitments, inferredCommitments);
    }

    internal IQueryable<ProviderDirectDebitCommitmentRow> BuildDirectDebitQuery(Guid userId, int limit)
    {
        return dbContext.BankDirectDebits
            .AsNoTracking()
            .Where(debit => debit.LinkedBankAccount != null
                && debit.LinkedBankAccount.Connection != null
                && debit.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(debit => debit.NextPaymentDateUtc == null)
            .ThenBy(debit => debit.NextPaymentDateUtc)
            .ThenBy(debit => debit.MerchantName ?? debit.Reference)
            .ThenBy(debit => debit.Id)
            .Take(limit)
            .Select(debit => new ProviderDirectDebitCommitmentRow
            {
                Id = debit.Id,
                LinkedBankAccountId = debit.LinkedBankAccountId,
                FinancialAccountId = debit.LinkedBankAccount!.FinancialAccountId,
                AccountDisplayName = debit.LinkedBankAccount.DisplayName,
                Status = debit.Status,
                MandateType = debit.MandateType,
                Reference = debit.Reference,
                MerchantName = debit.MerchantName,
                PreviousPaymentDateUtc = debit.PreviousPaymentDateUtc,
                PreviousPaymentAmount = debit.PreviousPaymentAmount,
                PreviousPaymentCurrency = debit.PreviousPaymentCurrency,
                NextPaymentDateUtc = debit.NextPaymentDateUtc,
                NextPaymentAmount = debit.NextPaymentAmount,
                NextPaymentCurrency = debit.NextPaymentCurrency,
                UpdatedUtc = debit.UpdatedUtc
            });
    }

    internal IQueryable<ProviderStandingOrderCommitmentRow> BuildStandingOrderQuery(Guid userId, int limit)
    {
        return dbContext.BankStandingOrders
            .AsNoTracking()
            .Where(order => order.LinkedBankAccount != null
                && order.LinkedBankAccount.Connection != null
                && order.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(order => order.NextPaymentDateUtc == null)
            .ThenBy(order => order.NextPaymentDateUtc)
            .ThenBy(order => order.PayeeName ?? order.Reference)
            .ThenBy(order => order.Id)
            .Take(limit)
            .Select(order => new ProviderStandingOrderCommitmentRow
            {
                Id = order.Id,
                LinkedBankAccountId = order.LinkedBankAccountId,
                FinancialAccountId = order.LinkedBankAccount!.FinancialAccountId,
                AccountDisplayName = order.LinkedBankAccount.DisplayName,
                Status = order.Status,
                Frequency = order.Frequency,
                Reference = order.Reference,
                PayeeName = order.PayeeName,
                FirstPaymentDateUtc = order.FirstPaymentDateUtc,
                NextPaymentDateUtc = order.NextPaymentDateUtc,
                FinalPaymentDateUtc = order.FinalPaymentDateUtc,
                NextPaymentAmount = order.NextPaymentAmount,
                NextPaymentCurrency = order.NextPaymentCurrency,
                UpdatedUtc = order.UpdatedUtc
            });
    }

    internal IQueryable<InferredCommitmentTransactionRow> BuildInferredTransactionQuery(
        Guid userId,
        DateTime utcNow,
        int limit)
    {
        var fromUtc = utcNow - InferredLookbackWindow;
        var throughUtc = utcNow + FutureTimestampTolerance;

        return dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.FinancialAccount != null
                && transaction.FinancialAccount.UserId == userId
                && transaction.Amount < 0m
                && transaction.BookedAtUtc >= fromUtc
                && transaction.BookedAtUtc <= throughUtc
                && transaction.TransferKind == null
                && transaction.LinkedTransferTransactionId == null
                && transaction.DeterministicLinkedTransactionId == null
                && !dbContext.TransactionRelationships.Any(relationship =>
                    relationship.RelationshipStatus == TransactionRelationshipStatus.Active
                    && relationship.AnalyticsTreatment != null
                    && relationship.AnalyticsTreatment.StartsWith("exclude_income_expense")
                    && (relationship.SourceTransactionId == transaction.Id
                        || relationship.TargetTransactionId == transaction.Id)))
            .OrderByDescending(transaction => transaction.BookedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(limit)
            .Select(transaction => new InferredCommitmentTransactionRow
            {
                Id = transaction.Id,
                FinancialAccountId = transaction.FinancialAccountId,
                LinkedBankAccountId = dbContext.LinkedBankAccounts
                    .Where(account => account.FinancialAccountId == transaction.FinancialAccountId)
                    .OrderBy(account => account.Id)
                    .Select(account => (Guid?)account.Id)
                    .FirstOrDefault(),
                AccountDisplayName = transaction.FinancialAccount!.Name,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Description = transaction.Description,
                BookedAtUtc = transaction.BookedAtUtc,
                CreatedUtc = transaction.CreatedUtc,
                MetadataUpdatedUtc = transaction.MetadataUpdatedUtc
            });
    }
}
