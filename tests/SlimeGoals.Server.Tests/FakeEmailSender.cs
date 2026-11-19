using SlimeGoals.Server.Accounts;

namespace SlimeGoals.Server.Tests;

/// <summary>
/// Guarda os envios na memória para os testes lerem o token de confirmação. O envio sai em segundo plano: quem
/// acabou de cadastrar espera o e-mail com <see cref="WaitForTokenAsync(string)"/>, e quem confere quantos saíram
/// espera a fila esvaziar antes (<see cref="EmailQueueExtensions.WaitForEmailQueueAsync"/>).
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    /// <summary>Espera padrão por um e-mail; folgada, porque o envio falso termina na hora.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly List<(string Email, string Token)> _sent = [];
    private readonly Dictionary<string, int> _read = new(StringComparer.Ordinal);
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count
    {
        get { lock (_lock) { return _sent.Count; } }
    }

    public string LastEmail
    {
        get { lock (_lock) { return _sent[^1].Email; } }
    }

    public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
    {
        TaskCompletionSource arrived;
        lock (_lock)
        {
            _sent.Add((email, token));
            arrived = _arrived;
            _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        arrived.TrySetResult();
        return Task.CompletedTask;
    }

    public Task<string> WaitForTokenAsync(string email) => WaitForTokenAsync(email, DefaultTimeout);

    /// <summary>
    /// O token do próximo e-mail ainda não lido para o endereço (já normalizado), na ordem de envio; espera até
    /// <paramref name="timeout"/> se ele ainda não saiu. Dois cadastros seguidos pedem duas leituras.
    /// </summary>
    public async Task<string> WaitForTokenAsync(string email, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (true)
        {
            Task arrived;
            lock (_lock)
            {
                var read = _read.GetValueOrDefault(email);
                var unread = _sent.Where(s => s.Email == email).Skip(read).Select(s => s.Token).FirstOrDefault();
                if (unread is not null)
                {
                    _read[email] = read + 1;
                    return unread;
                }

                arrived = _arrived.Task;
            }

            try
            {
                await arrived.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Nenhum e-mail novo para {email} em {timeout}.");
            }
        }
    }
}
