using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace SlimeGoals.Server.Accounts;

/// <summary>Um e-mail de confirmação à espera de envio; o hash do token apaga a conta pendente se o envio falhar.</summary>
public sealed record ConfirmationEmail(string Email, string Token, byte[] TokenHash);

/// <summary>
/// Fila em memória dos e-mails de confirmação, lida por <see cref="EmailDispatchService"/>. A capacidade conta os
/// e-mails à espera e o que está sendo enviado: cheia, <see cref="TryEnqueue"/> recusa na hora, sem esperar.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "É uma fila de verdade: entra por TryEnqueue e sai em ordem.")]
public sealed class ConfirmationEmailQueue
{
    public const int DefaultCapacity = 100;

    private readonly Channel<ConfirmationEmail> _channel;
    private readonly int _capacity;
    private readonly Lock _lock = new();
    private int _pending;
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConfirmationEmailQueue(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _channel = Channel.CreateBounded<ConfirmationEmail>(new BoundedChannelOptions(capacity) { SingleReader = true });
        _idle.SetResult();
    }

    /// <summary>Põe o e-mail na fila; devolve <c>false</c> se ela estiver cheia.</summary>
    public bool TryEnqueue(ConfirmationEmail email)
    {
        lock (_lock)
        {
            if (_pending >= _capacity || !_channel.Writer.TryWrite(email))
            {
                return false;
            }

            if (_pending++ == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        return true;
    }

    public IAsyncEnumerable<ConfirmationEmail> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>O trabalhador terminou um e-mail (enviado ou desistido) e libera a vaga dele.</summary>
    internal void Complete()
    {
        TaskCompletionSource? idle = null;
        lock (_lock)
        {
            if (--_pending == 0)
            {
                idle = _idle;
            }
        }

        idle?.TrySetResult();
    }

    /// <summary>Termina quando não há e-mail à espera nem sendo enviado; os testes esperam a fila por ele.</summary>
    internal Task WhenIdleAsync()
    {
        lock (_lock)
        {
            return _idle.Task;
        }
    }
}
