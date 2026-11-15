#if SYNC_DEV
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Sync.Vault;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Cofre do Android (§6): uma chave AES-256 criada dentro do Keystore, que nunca sai dele, cifra cada segredo em GCM; o
/// resultado fica num arquivo privado em <c>no_backup/cofre</c>, no formato de <see cref="SealedSecretFormat"/>. Sem o
/// Keystore, nada é gravado. Segredo que existe mas não abre (chave perdida, arquivo corrompido) é
/// <see cref="InvalidDataException"/>: a pessoa cai em "sessão ilegível" e pode sair.
/// </summary>
internal sealed class AndroidKeystoreVault(Context context) : ISecretVault
{
    private const string Provider = "AndroidKeyStore";
    private const string KeyAlias = "slimegoals-cofre";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int TagBits = SealedSecretFormat.TagSize * 8;

    private readonly Lock _lock = new();

    private string Directory => Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "cofre");

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            LoadKeyStore();
            return true;
        }
        catch (SecretVaultUnavailableException)
        {
            return false;
        }
    }, ct);

    public Task<byte[]?> GetAsync(string name, CancellationToken ct = default) => Task.Run(() =>
    {
        var path = PathOf(name);
        lock (_lock)
        {
            if (!File.Exists(path))
                return null;
            var blob = File.ReadAllBytes(path);
            if (!SealedSecretFormat.TryUnpack(blob, out var nonce, out var cipherAndTag))
                throw new InvalidDataException("O segredo do cofre está em formato desconhecido.");
            var store = LoadKeyStore();
            try
            {
                var key = store.GetKey(KeyAlias, null)
                    ?? throw new InvalidDataException("A chave do cofre não existe mais.");
                using var cipher = Cipher.GetInstance(Transformation)!;
                cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(TagBits, nonce));
                cipher.UpdateAAD(SealedSecretFormat.AssociatedData(name));
                return (byte[]?)cipher.DoFinal(cipherAndTag)!;
            }
            catch (Java.Lang.Throwable)
            {
                // Tag inválida, chave invalidada, irrecuperável ou recusada pelo Keystore (GeneralSecurityException,
                // ProviderException, IllegalStateException...): o segredo não abre, e a pessoa pode sair. A mensagem
                // do Java não vai adiante.
                throw new InvalidDataException("O segredo do cofre não abre.");
            }
        }
    }, ct);

    public Task SetAsync(string name, byte[] secret, CancellationToken ct = default) => Task.Run(() =>
    {
        var path = PathOf(name);
        lock (_lock)
        {
            var key = LoadKeyStore().GetKey(KeyAlias, null) ?? CreateKey();
            byte[] blob;
            try
            {
                using var cipher = Cipher.GetInstance(Transformation)!;
                // O Keystore escolhe o nonce: cifra aleatória é exigida por padrão.
                cipher.Init(CipherMode.EncryptMode, key);
                cipher.UpdateAAD(SealedSecretFormat.AssociatedData(name));
                var cipherAndTag = cipher.DoFinal(secret)!;
                blob = SealedSecretFormat.Pack(cipher.GetIV()!, cipherAndTag);
            }
            catch (GeneralSecurityException e)
            {
                throw new SecretVaultUnavailableException("O cofre do Android recusou a gravação.", e);
            }

            System.IO.Directory.CreateDirectory(Directory);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, blob);
            File.Move(temp, path, overwrite: true);
        }
    }, ct);

    public Task DeleteAsync(string name, CancellationToken ct = default) => Task.Run(() =>
    {
        var path = PathOf(name);
        lock (_lock)
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }, ct);

    private string PathOf(string name)
    {
        if (!SealedSecretFormat.IsValidName(name))
            throw new ArgumentException("Nome de segredo inválido.", nameof(name));
        return Path.Combine(Directory, name + ".bin");
    }

    private static KeyStore LoadKeyStore()
    {
        try
        {
            var store = KeyStore.GetInstance(Provider)!;
            store.Load(null);
            return store;
        }
        catch (Exception e) when (e is KeyStoreException or Java.IO.IOException or GeneralSecurityException)
        {
            throw new SecretVaultUnavailableException("O cofre do Android não está disponível.", e);
        }
    }

    // GetKey devolve só IKey: a classe Java da chave do Keystore é oculta, sem binding, e "is ISecretKey" daria falso
    // mesmo com a chave lá. Por isso o código trata a chave como IKey, que é o que o Cipher.Init recebe.
    private static IKey CreateKey()
    {
        try
        {
            var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)!;
            generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetKeySize(256)
                .Build());
            return generator.GenerateKey()!;
        }
        catch (GeneralSecurityException e)
        {
            throw new SecretVaultUnavailableException("O cofre do Android não criou a chave.", e);
        }
    }
}
#endif
