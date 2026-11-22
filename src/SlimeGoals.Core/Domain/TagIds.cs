using System.Security.Cryptography;
using System.Text;

namespace SlimeGoals.Core.Domain;

/// <summary>
/// Ids derivados (UUID v5 com SHA-256 sobre UTF-8): o mesmo nome gera o mesmo id em qualquer
/// aparelho, então a tag e a ligação convergem sem coordenação.
/// </summary>
public static class TagIds
{
    /// <summary>Id da tag de um nome já normalizado.</summary>
    public static Guid ForTag(string normalizedName) => FromText("slimegoals:tag:" + normalizedName);

    /// <summary>Id da ligação entre uma meta e uma tag (nome já normalizado).</summary>
    public static Guid ForLink(Guid goalId, string normalizedName) =>
        FromText("slimegoals:goal-tag:" + goalId.ToString("D") + ":" + normalizedName);

    private static Guid FromText(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
