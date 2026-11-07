using SlimeGoals.Server.Accounts;

namespace SlimeGoals.Server.Tests;

/// <summary>Guarda os envios na memória para os testes lerem o token de confirmação.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<(string Email, string Token)> _sent = [];

    public int Count
    {
        get { lock (_sent) { return _sent.Count; } }
    }

    public string LastToken
    {
        get { lock (_sent) { return _sent[^1].Token; } }
    }

    public string LastEmail
    {
        get { lock (_sent) { return _sent[^1].Email; } }
    }

    public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add((email, token));
        }

        return Task.CompletedTask;
    }
}
