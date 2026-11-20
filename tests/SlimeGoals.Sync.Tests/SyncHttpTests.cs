using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SlimeGoals.Sync.Tests;

public class SyncHttpTests
{
    [Fact]
    public async Task Does_not_follow_a_redirect()
    {
        using var server = new RedirectingServer();
        using var http = SyncHttp.Create(server.Address);

        using var response = await http.GetAsync("v1/sync", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        server.Paths.Should().Equal("/v1/sync");
    }

    /// <summary>Responde 302 para <c>/outro</c> a qualquer pedido, e 200 ao <c>/outro</c>; guarda os caminhos pedidos.</summary>
    private sealed class RedirectingServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public RedirectingServer()
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(ServeAsync);
        }

        public Uri Address { get; }

        public ConcurrentQueue<string> Paths { get; } = new();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    var path = await ReadPathAsync(stream);
                    Paths.Enqueue(path);
                    var reply = path == "/outro"
                        ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : $"HTTP/1.1 302 Found\r\nLocation: {Address}outro\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>Lê os cabeçalhos até a linha vazia e devolve o caminho da primeira linha.</summary>
        private async Task<string> ReadPathAsync(NetworkStream stream)
        {
            var received = new StringBuilder();
            var buffer = new byte[1024];
            while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0)
                    break;
                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            return received.ToString().Split(' ')[1];
        }
    }
}
