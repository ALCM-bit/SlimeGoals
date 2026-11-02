using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SlimeGoals.Platform.Linux.SingleInstance;

/// <summary>
/// Instância única por banco de dados (decisão 2 do plano da Fase 4d).
/// A trava é um <see cref="FileStream"/> com <see cref="FileShare.None"/> no <c>.lock</c>
/// (no Linux o .NET usa <c>flock(LOCK_EX | LOCK_NB)</c>, que também conflita dentro do mesmo processo);
/// o socket Unix no <c>.sock</c> recebe o pedido <c>show</c> da segunda instância e responde <c>ok</c>.
/// </summary>
/// <remarks>
/// O <c>.lock</c> fica no disco ao liberar: apagá-lo abriria uma corrida em que outro processo
/// trava o arquivo antigo enquanto um terceiro cria um novo com o mesmo nome. O arquivo vazio é inofensivo.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string ShowLine = "show";
    private const string OkReply = "ok\n";
    private const int MaxLineBytes = 64;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly FileStream _lock;
    private readonly Socket _listener;
    private readonly string _socketPath;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    private SingleInstanceGuard(FileStream lockStream, Socket listener, string socketPath)
    {
        _lock = lockStream;
        _listener = listener;
        _socketPath = socketPath;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>Disparado (em thread do pool) a cada pedido <c>show</c> recebido.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// Primeira instância: devolve o guarda (trava presa, socket ouvindo). Já existe outra: <c>null</c>.
    /// </summary>
    /// <param name="runtimeDirectory">Pasta da trava e do socket; criada com permissão 0700 se faltar.</param>
    /// <param name="key">Caminho completo do banco; vira o hash do nome dos arquivos.</param>
    public static SingleInstanceGuard? TryAcquire(string runtimeDirectory, string key)
    {
        EnsureDirectory(runtimeDirectory);
        var (lockPath, socketPath) = FilePaths(runtimeDirectory, key);

        FileStream lockStream;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            // Normalmente é outra instância segurando a trava; o texto ajuda a ver disco cheio ou somente leitura.
            Trace.TraceInformation($"SingleInstanceGuard: trava {lockPath} indisponível: {ex.Message}");
            return null;
        }

        Socket? listener = null;
        try
        {
            // Com a trava em mãos, um .sock existente é órfão de um processo que caiu.
            if (File.Exists(socketPath))
                File.Delete(socketPath);

            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(8);
            return new SingleInstanceGuard(lockStream, listener, socketPath);
        }
        catch
        {
            listener?.Dispose();
            lockStream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Pede à instância existente para mostrar a janela; <c>true</c> se ela respondeu <c>ok</c> em até 2 s.
    /// Qualquer falha (ninguém ouvindo, socket ausente, tempo esgotado) devolve <c>false</c>.
    /// </summary>
    public static bool SignalExisting(string runtimeDirectory, string key)
    {
        var (_, socketPath) = FilePaths(runtimeDirectory, key);
        try
        {
            return SignalAsync(socketPath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"SingleInstanceGuard: sinal para {socketPath} falhou: {ex.Message}");
            return false;
        }
    }

    /// <summary>Para de ouvir, apaga o socket e libera a trava.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        _listener.Dispose();
        try
        {
            File.Delete(_socketPath);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"SingleInstanceGuard: não consegui apagar {_socketPath}: {ex}");
        }
        // Libera a trava só depois de apagar o socket: quem travar em seguida cria o seu sem disputa.
        _lock.Dispose();
        _cts.Dispose();
    }

    internal static (string LockPath, string SocketPath) FilePaths(string runtimeDirectory, string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];
        var stem = Path.Combine(runtimeDirectory, $"slimegoals-{hash}");
        return (stem + ".lock", stem + ".sock");
    }

    private static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path))
            return;
        // Este projeto só roda no Linux; a guarda satisfaz o analisador de plataforma (CA1416).
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async Task<bool> SignalAsync(string socketPath)
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cts.Token).ConfigureAwait(false);
        await client.SendAsync(Encoding.UTF8.GetBytes(ShowLine + "\n"), SocketFlags.None, cts.Token).ConfigureAwait(false);
        var reply = await ReadLineAsync(client, cts.Token).ConfigureAwait(false);
        return reply == "ok";
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"SingleInstanceGuard: falha ao aceitar conexão: {ex}");
                // Pausa curta: um erro persistente (ex.: EMFILE) não pode girar o laço sem parar.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(Socket client, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(Timeout);
                var line = await ReadLineAsync(client, cts.Token).ConfigureAwait(false);
                if (line != ShowLine)
                    return;

                await client.SendAsync(Encoding.UTF8.GetBytes(OkReply), SocketFlags.None, cts.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _disposed) == 0)
                    ThreadPool.QueueUserWorkItem(_ => RaiseShowRequested());
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            Trace.TraceWarning($"SingleInstanceGuard: conexão interrompida no encerramento: {ex.Message}");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"SingleInstanceGuard: falha ao tratar conexão: {ex}");
        }
    }

    private void RaiseShowRequested()
    {
        // O guarda pode ter sido descartado entre a fila e a execução: depois do Dispose, nenhum aviso.
        if (Volatile.Read(ref _disposed) != 0)
            return;
        try
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"SingleInstanceGuard: falha no ShowRequested: {ex}");
        }
    }

    /// <summary>Lê até <c>\n</c> (sem incluí-lo) ou até o outro lado fechar; linhas longas demais viram <c>null</c>.</summary>
    private static async Task<string?> ReadLineAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[MaxLineBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(length), SocketFlags.None, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            var newline = Array.IndexOf(buffer, (byte)'\n', length, read);
            length += read;
            if (newline >= 0)
                return Encoding.UTF8.GetString(buffer, 0, newline);
        }
        return null;
    }
}
