namespace PasteDock.Core.Storage;

public interface IContentProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
