namespace SlimeGoals.Core.Sync;

public interface ISyncCrypto
{
    string RecordKey(Guid recordId);
    byte[] Seal(SyncEnvelope envelope);
    SyncEnvelope Open(string recordKey, byte[] blob); // lança SyncFormatException em versão desconhecida
}
