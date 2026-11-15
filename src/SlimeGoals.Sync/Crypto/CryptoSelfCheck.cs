namespace SlimeGoals.Sync.Crypto;

/// <summary>Um vetor fixo conferido: o nome da saída e se ela bateu.</summary>
public sealed record CryptoCheckResult(string Name, bool Passed);

/// <summary>
/// Os vetores fixos da spec ("Decisões da fase 9.4"), calculados pelas classes de produção. O APK de desenvolvimento
/// roda esta conferência no aparelho (Konscious no arm64, Release com trimming). Os vetores são públicos: o resultado
/// diz só se cada um bateu.
/// </summary>
public static class CryptoSelfCheck
{
    private const string Password = "correct horse battery";
    private const string RecoveryCodeText = "000G-40R4-0M30-E209-185G-R38E-1W";
    private static readonly Guid RecordId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>Calcula os 5 vetores, cada um isolado: uma exceção vira falha só daquele item.</summary>
    public static IReadOnlyList<CryptoCheckResult> Run()
    {
        byte[] salt = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        byte[] dataKey = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
        return
        [
            Check("auth_key", "ba53acc24ffa9ba24189d1f5b7946535d1ee123feaef82cc6d9b5cfbb2f172bc", () =>
            {
                using var keys = PasswordKeys.Derive(Password, salt, KdfParams.Default);
                return Convert.ToHexStringLower(keys.AuthKey);
            }),
            Check("wrap_key", "67c2cecefb41882ac98e816bab7619c23f0dc2d30a089743bb33c67ed79bc552", () =>
            {
                using var keys = PasswordKeys.Derive(Password, salt, KdfParams.Default);
                return Convert.ToHexStringLower(keys.WrapKey);
            }),
            Check("recovery_key", "944be12c002a09efb7f8b498f2063f4b674d48a0d24e0bb8d4d814e625dd2159", () =>
            {
                using var code = RecoveryCode.Parse(RecoveryCodeText);
                using var keys = code.Keys();
                return Convert.ToHexStringLower(keys.RecoveryKey);
            }),
            Check("recovery_wrap", "c7da2cd9178e6498001e21d80eead4bdd5ea7273c354e309ad2bd7fe4e1e3eef", () =>
            {
                using var code = RecoveryCode.Parse(RecoveryCodeText);
                using var keys = code.Keys();
                return Convert.ToHexStringLower(keys.WrapKey);
            }),
            Check("record_key", "QDTsiTp-QhBeJKBWIU_Iu_FbuyRZgQnQWvn4-NzxRxY", () =>
            {
                using var crypto = new AesGcmSyncCrypto(dataKey);
                return crypto.RecordKey(RecordId);
            }),
        ];
    }

    private static CryptoCheckResult Check(string name, string expected, Func<string> compute)
    {
        try
        {
            return new(name, compute() == expected);
        }
        catch (Exception)
        {
            return new(name, false);
        }
    }
}
