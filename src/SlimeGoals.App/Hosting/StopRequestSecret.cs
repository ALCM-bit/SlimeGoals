using System.Security.Cryptography;
using System.Text;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Código que só o "Parar" da notificação do Android conhece. A tela principal é exportada (o launcher a abre), então
/// qualquer app pode abri-la com qualquer extra: sem o código, o pedido de parar é ignorado. Criado uma vez por
/// instalação e guardado na área privada do app, fora do banco que sincroniza.
/// </summary>
public static class StopRequestSecret
{
    /// <summary>256 bits aleatórios, em hexadecimal.</summary>
    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Se o pedido traz o código guardado. Sem código guardado, nada confere: um pedido vazio não pode passar.
    /// Comparação em tempo constante.
    /// </summary>
    public static bool Matches(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(received));
    }
}
