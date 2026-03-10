namespace NSFinance.Api.Modules.Banking.Services;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
