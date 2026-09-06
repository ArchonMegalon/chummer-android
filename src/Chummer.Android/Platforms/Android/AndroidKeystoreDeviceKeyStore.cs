using Android.Security.Keystore;
using Java.Security;
using Java.Security.Cert;
using Java.Security.Spec;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Chummer.Android.Platform;

[SupportedOSPlatform("android24.0")]
public sealed class AndroidKeystoreDeviceKeyStore : IAndroidDeviceKeyStore
{
    private const string Provider = "AndroidKeyStore";
    private const int KeySizeBits = 2048;

    public Task<AndroidDevicePublicKey> CreateAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        ValidateAlias(alias);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using KeyPairGenerator generator = KeyPairGenerator.GetInstance(
                KeyProperties.KeyAlgorithmRsa,
                Provider)!;
            using KeyGenParameterSpec specification = new KeyGenParameterSpec.Builder(
                    alias,
                    KeyStorePurpose.Sign | KeyStorePurpose.Verify)
                .SetKeySize(KeySizeBits)
                .SetDigests(KeyProperties.DigestSha256)
                .SetSignaturePaddings(KeyProperties.SignaturePaddingRsaPkcs1)
                .Build();
            generator.Initialize(specification);
            using KeyPair generated = generator.GenerateKeyPair()!;
            IPrivateKey? generatedPrivateKey = generated.Private;
            IPublicKey? generatedPublicKey = generated.Public;
            if (generatedPrivateKey is null || generatedPublicKey is null)
            {
                throw new CryptographicException("Android Keystore returned an incomplete RSA key pair.");
            }
            RequireNonExportable(generatedPrivateKey);
            // Key generation is persistent. Once GenerateKeyPair succeeds this method must
            // return its public authority; observing cancellation here would orphan the alias.
            return Task.FromResult(new AndroidDevicePublicKey(
                AndroidDeviceKeyAvailability.Available,
                ExportProtocolPublicKey(generatedPublicKey)));
        }
        catch (KeyPermanentlyInvalidatedException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (GeneralSecurityException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (CryptographicException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
    }

    public Task<AndroidDevicePublicKey> GetPublicKeyAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        ValidateAlias(alias);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using KeyStore keyStore = OpenKeyStore();
            if (!keyStore.ContainsAlias(alias))
            {
                return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Missing));
            }

            using Certificate? certificate = keyStore.GetCertificate(alias);
            if (certificate?.PublicKey is null)
            {
                return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
            }

            using IKey? key = keyStore.GetKey(alias, null);
            if (key is not IPrivateKey privateKey)
            {
                return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
            }
            RequireNonExportable(privateKey);
            // Initializing a signer is the earliest non-mutating probe Android exposes for a
            // permanently invalidated private key. Detect it while resuming the explicit link,
            // before a request body or packet proof is constructed.
            using Signature signer = Signature.GetInstance("SHA256withRSA")!;
            signer.InitSign(privateKey);

            return Task.FromResult(new AndroidDevicePublicKey(
                AndroidDeviceKeyAvailability.Available,
                ExportProtocolPublicKey(certificate.PublicKey)));
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
        }
        catch (UnrecoverableKeyException)
        {
            return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
        }
        catch (GeneralSecurityException)
        {
            return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
        }
        catch (CryptographicException)
        {
            return Task.FromResult(new AndroidDevicePublicKey(AndroidDeviceKeyAvailability.Invalidated));
        }
    }

    public Task<byte[]> SignAsync(
        string alias,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ValidateAlias(alias);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using KeyStore keyStore = OpenKeyStore();
            using IKey? key = keyStore.GetKey(alias, null);
            if (key is not IPrivateKey privateKey)
            {
                throw RelinkRequired(AndroidDeviceKeyAvailability.Missing);
            }
            RequireNonExportable(privateKey);

            using Signature signer = Signature.GetInstance("SHA256withRSA")!;
            signer.InitSign(privateKey);
            byte[] bytes = payload.ToArray();
            try
            {
                signer.Update(bytes);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(signer.Sign()!);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (AndroidDeviceRelinkRequiredException)
        {
            throw;
        }
        catch (KeyPermanentlyInvalidatedException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (UnrecoverableKeyException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (InvalidKeyException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (GeneralSecurityException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
        catch (CryptographicException exception)
        {
            throw RelinkRequired(AndroidDeviceKeyAvailability.Invalidated, exception);
        }
    }

    public Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
    {
        ValidateAlias(alias);
        cancellationToken.ThrowIfCancellationRequested();
        using KeyStore keyStore = OpenKeyStore();
        if (keyStore.ContainsAlias(alias))
        {
            keyStore.DeleteEntry(alias);
        }

        return Task.CompletedTask;
    }

    private static KeyStore OpenKeyStore()
    {
        KeyStore keyStore = KeyStore.GetInstance(Provider)!;
        keyStore.Load(null);
        return keyStore;
    }

    private static string ExportProtocolPublicKey(IPublicKey publicKey)
    {
        byte[]? encoded = publicKey.GetEncoded();
        if (encoded is null || encoded.Length == 0)
        {
            throw new CryptographicException("Android Keystore returned no RSA public key.");
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(encoded, out int bytesRead);
            if (bytesRead != encoded.Length || rsa.KeySize < KeySizeBits)
            {
                throw new CryptographicException("Android Keystore returned an invalid RSA public key.");
            }

            return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static void RequireNonExportable(IPrivateKey privateKey)
    {
        byte[]? encoded = null;
        try
        {
            encoded = privateKey.GetEncoded();
            if (encoded is { Length: > 0 })
            {
                throw new CryptographicException(
                    "Android Keystore returned an exportable account-link private key.");
            }
        }
        finally
        {
            if (encoded is not null)
            {
                CryptographicOperations.ZeroMemory(encoded);
            }
        }
    }

    private static void ValidateAlias(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        if (!AndroidAccountLinkKeyAuthority.IsExpectedAlias(alias))
        {
            throw new ArgumentException("The Android Keystore alias is outside the account-link scope.", nameof(alias));
        }
    }

    private static AndroidDeviceRelinkRequiredException RelinkRequired(
        AndroidDeviceKeyAvailability availability,
        Exception? exception = null)
        => new(
            availability,
            "The Android Keystore account-link key is unavailable. Start a fresh account link.",
            exception);
}
