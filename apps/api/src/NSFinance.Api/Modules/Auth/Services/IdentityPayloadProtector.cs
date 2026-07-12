using Microsoft.AspNetCore.DataProtection;

namespace NSFinance.Api.Modules.Auth.Services;

public sealed class IdentityPayloadProtector(IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "NSFinance.Identity.TransactionalMessagePayload.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
