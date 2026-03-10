using Microsoft.AspNetCore.DataProtection;

namespace NSFinance.Api.Modules.Banking.Services;

public sealed class DataProtectionSecretProtector(IDataProtectionProvider dataProtectionProvider) : ISecretProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("NSFinance.OpenBankingTokens.v1");

    public string Protect(string plaintext)
    {
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        return _protector.Unprotect(ciphertext);
    }
}
