namespace SpecFleet.Service.Features.Chat.Secrets;

/// <summary>
/// Encrypts provider credentials for storage. The key is supplied by deployment
/// configuration and is never persisted alongside the ciphertext.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
