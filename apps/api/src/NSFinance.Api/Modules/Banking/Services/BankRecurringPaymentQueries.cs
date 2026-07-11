using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Modules.Banking.DTOs;
using NSFinance.Api.Persistence;

namespace NSFinance.Api.Modules.Banking.Services;

internal static class BankRecurringPaymentQueries
{
    internal static IQueryable<BankDirectDebitDto> BuildDirectDebits(
        AppDbContext dbContext,
        Guid userId)
    {
        return dbContext.BankDirectDebits
            .AsNoTracking()
            .Where(x => x.LinkedBankAccount != null
                && x.LinkedBankAccount.Connection != null
                && x.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(x => x.NextPaymentDateUtc == null)
            .ThenBy(x => x.NextPaymentDateUtc)
            .ThenBy(x => x.LinkedBankAccount!.DisplayName)
            .ThenBy(x => x.Id)
            .Select(x => new BankDirectDebitDto(
                x.Id,
                x.LinkedBankAccountId,
                x.LinkedBankAccount!.ConnectionId,
                x.LinkedBankAccount.DisplayName,
                x.ProviderDirectDebitId,
                x.Status,
                x.MandateType,
                x.Reference,
                x.MerchantName,
                x.PreviousPaymentDateUtc,
                x.PreviousPaymentAmount,
                x.PreviousPaymentCurrency,
                x.NextPaymentDateUtc,
                x.NextPaymentAmount,
                x.NextPaymentCurrency,
                x.UpdatedUtc));
    }

    internal static IQueryable<BankStandingOrderDto> BuildStandingOrders(
        AppDbContext dbContext,
        Guid userId)
    {
        return dbContext.BankStandingOrders
            .AsNoTracking()
            .Where(x => x.LinkedBankAccount != null
                && x.LinkedBankAccount.Connection != null
                && x.LinkedBankAccount.Connection.UserId == userId)
            .OrderBy(x => x.NextPaymentDateUtc == null)
            .ThenBy(x => x.NextPaymentDateUtc)
            .ThenBy(x => x.LinkedBankAccount!.DisplayName)
            .ThenBy(x => x.Id)
            .Select(x => new BankStandingOrderDto(
                x.Id,
                x.LinkedBankAccountId,
                x.LinkedBankAccount!.ConnectionId,
                x.LinkedBankAccount.DisplayName,
                x.ProviderStandingOrderId,
                x.Status,
                x.Frequency,
                x.Reference,
                x.PayeeName,
                x.FirstPaymentDateUtc,
                x.NextPaymentDateUtc,
                x.FinalPaymentDateUtc,
                x.NextPaymentAmount,
                x.NextPaymentCurrency,
                x.UpdatedUtc));
    }
}
