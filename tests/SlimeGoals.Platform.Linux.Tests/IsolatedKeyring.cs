using System.Diagnostics;
using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>
/// Barramento de sessão privado (<c>dbus-daemon</c> com configuração própria, sem ativação de serviços) e, se
/// pedido, um <c>gnome-keyring-daemon</c> isolado nele. HOME e XDG apontam para um diretório temporário e o
/// ambiente é limpo, então o chaveiro de verdade do usuário e o barramento da sessão real nunca são tocados.
/// Descartar mata os processos e apaga o diretório.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class IsolatedKeyring : IAsyncDisposable
{
    public const string Service = "org.freedesktop.secrets";
    public const string ServicePath = "/org/freedesktop/secrets";
    public const string ServiceInterface = "org.freedesktop.Secret.Service";

    private readonly DirectoryInfo _root;
    private readonly List<Process> _processes = [];

    private IsolatedKeyring(DirectoryInfo root) => _root = root;

    /// <summary>Endereço do barramento privado.</summary>
    public string Address { get; private set; } = "";

    /// <summary>
    /// Sobe o barramento e, com <paramref name="keyring"/>, o chaveiro. <paramref name="loginPassword"/> vai pela
    /// entrada padrão do <c>--unlock</c>: uma linha (mesmo vazia) cria e destrava o chaveiro <c>login</c>, que vira
    /// o alias <c>default</c>; <c>null</c> não manda nada, e o chaveiro fica sem coleção <c>default</c>.
    /// </summary>
    public static async Task<IsolatedKeyring> StartAsync(bool keyring = true, string? loginPassword = "")
    {
        var dbusDaemon = FindBinary("dbus-daemon");
        var keyringDaemon = FindBinary("gnome-keyring-daemon");
        if (dbusDaemon is null) Assert.Skip("dbus-daemon ausente");
        if (keyring && keyringDaemon is null) Assert.Skip("gnome-keyring-daemon ausente");

        // Caminho curto: socket Unix tem limite de ~108 bytes.
        var root = Directory.CreateTempSubdirectory("sgk-");
        var isolated = new IsolatedKeyring(root);
        try
        {
            await isolated.StartBusAsync(dbusDaemon!);
            if (keyring) await isolated.StartKeyringAsync(keyringDaemon!, loginPassword);
            return isolated;
        }
        catch
        {
            await isolated.DisposeAsync();
            throw;
        }
    }

    private async Task StartBusAsync(string dbusDaemon)
    {
        var config = Path.Combine(_root.FullName, "bus.conf");
        await File.WriteAllTextAsync(config, $"""
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN"
              "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig>
              <type>session</type>
              <listen>unix:path={Path.Combine(_root.FullName, "bus")}</listen>
              <auth>EXTERNAL</auth>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """);
        var start = IsolatedStart(dbusDaemon, $"--config-file={config}", "--nofork", "--print-address");
        start.RedirectStandardOutput = true;
        var bus = Track(Process.Start(start)!);
        var line = await bus.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException("dbus-daemon não informou o endereço.");
        Address = line.Trim();
    }

    private async Task StartKeyringAsync(string keyringDaemon, string? loginPassword)
    {
        var runtime = Directory.CreateDirectory(Path.Combine(_root.FullName, "run"));
        File.SetUnixFileMode(runtime.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = IsolatedStart(keyringDaemon, "--foreground", "--unlock", "--components=secrets",
            $"--control-directory={Path.Combine(runtime.FullName, "keyring")}");
        start.RedirectStandardInput = true;
        start.Environment["HOME"] = _root.FullName;
        start.Environment["XDG_DATA_HOME"] = Directory.CreateDirectory(Path.Combine(_root.FullName, "data")).FullName;
        start.Environment["XDG_CONFIG_HOME"] = Directory.CreateDirectory(Path.Combine(_root.FullName, "config")).FullName;
        start.Environment["XDG_CACHE_HOME"] = Directory.CreateDirectory(Path.Combine(_root.FullName, "cache")).FullName;
        start.Environment["XDG_RUNTIME_DIR"] = runtime.FullName;
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] = Address;
        var keyring = Track(Process.Start(start)!);
        if (loginPassword is not null) await keyring.StandardInput.WriteLineAsync(loginPassword);
        keyring.StandardInput.Close();

        // Pronto quando o serviço responde e, se houve senha, quando o alias default já aponta para o login.
        using var connection = await ConnectAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                var alias = await ReadAliasAsync(connection, "default");
                if (loginPassword is null || alias != "/") return;
            }
            catch (DBusErrorReplyException) when (DateTime.UtcNow < deadline)
            {
                // ServiceUnknown enquanto o chaveiro não pegou o nome.
            }
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("gnome-keyring-daemon não ficou pronto.");
            await Task.Delay(50);
        }
    }

    /// <summary>Conexão nova e já conectada ao barramento privado.</summary>
    public async Task<DBusConnection> ConnectAsync()
    {
        var connection = new DBusConnection(Address);
        try
        {
            await connection.ConnectAsync();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static async Task<string> ReadAliasAsync(DBusConnection connection, string alias)
    {
        var message = Build(connection, ServicePath, ServiceInterface, "ReadAlias", "s",
            (ref MessageWriter w) => w.WriteString(alias));
        return await connection.CallMethodAsync(message, (m, _) => m.GetBodyReader().ReadObjectPathAsString(), null);
    }

    /// <summary>Tranca as coleções pela API (o chaveiro de teste não destrava de novo sem diálogo).</summary>
    public async Task LockAsync(string collection)
    {
        using var connection = await ConnectAsync();
        await connection.CallMethodAsync(Build(connection, ServicePath, ServiceInterface, "Lock", "ao",
            (ref MessageWriter w) => w.WriteArray(new ObjectPath[] { new(collection) })));
    }

    /// <summary>Itens que casam com os atributos, destravados e travados, pela API <c>SearchItems</c> do serviço.</summary>
    public async Task<IReadOnlyList<string>> SearchItemsAsync(IReadOnlyDictionary<string, string> attributes)
    {
        using var connection = await ConnectAsync();
        var message = Build(connection, ServicePath, ServiceInterface, "SearchItems", "a{ss}", (ref MessageWriter w) =>
        {
            var dict = w.WriteDictionaryStart();
            foreach (var (key, value) in attributes)
            {
                w.WriteDictionaryEntryStart();
                w.WriteString(key);
                w.WriteString(value);
            }
            w.WriteDictionaryEnd(dict);
        });
        return await connection.CallMethodAsync(message, (m, _) =>
        {
            var r = m.GetBodyReader();
            return r.ReadArrayOfObjectPath().Concat(r.ReadArrayOfObjectPath()).Select(p => p.ToString()).ToArray();
        }, null);
    }

    /// <summary>Rótulo e atributos de um item, lidos pelas propriedades D-Bus.</summary>
    public async Task<(string Label, IReadOnlyDictionary<string, string> Attributes)> ReadItemAsync(string item)
    {
        using var connection = await ConnectAsync();
        var label = await GetPropertyAsync(connection, item, "Label", (ref Reader r) => r.ReadString());
        var attributes = await GetPropertyAsync(connection, item, "Attributes", (ref Reader r) =>
        {
            var result = new Dictionary<string, string>();
            var end = r.ReadDictionaryStart();
            while (r.HasNext(end)) result[r.ReadString()] = r.ReadString();
            return (IReadOnlyDictionary<string, string>)result;
        });
        return (label, attributes);
    }

    private delegate T ValueReader<out T>(ref Reader reader);

    public delegate void BodyWriter(ref MessageWriter writer);

    /// <summary>Monta a chamada no mesmo método e passa o <see cref="MessageWriter"/> por <c>ref</c> (armadilha do spike).</summary>
    public static MessageBuffer Build(DBusConnection connection, string path, string @interface, string member,
        string? signature, BodyWriter? body = null)
    {
        var w = connection.GetMessageWriter();
        try
        {
            w.WriteMethodCallHeader(Service, path, @interface, member, signature, MessageFlags.None);
            body?.Invoke(ref w);
            return w.CreateMessage();
        }
        finally
        {
            w.Dispose();
        }
    }

    private static async Task<T> GetPropertyAsync<T>(DBusConnection connection, string item, string property,
        ValueReader<T> read)
    {
        var message = Build(connection, item, "org.freedesktop.DBus.Properties", "Get", "ss", (ref MessageWriter w) =>
        {
            w.WriteString("org.freedesktop.Secret.Item");
            w.WriteString(property);
        });
        return await connection.CallMethodAsync(message, (m, _) =>
        {
            var r = m.GetBodyReader();
            r.ReadSignatureAsString();
            return read(ref r);
        }, null);
    }

    /// <summary>
    /// Registra os nomes das chamadas de método enviadas a <c>org.freedesktop.secrets</c>, via
    /// <c>BecomeMonitor</c> no barramento privado.
    /// </summary>
    public async Task<CallRecorder> RecordCallsAsync()
    {
        var connection = await ConnectAsync();
        var recorder = new CallRecorder(this, connection);
        await connection.BecomeMonitorAsync((e, message) =>
        {
            if (e is not null) return;
            using (message)
            {
                var m = message.Message;
                if (m.MessageType == MessageType.MethodCall && m.DestinationAsString == Service)
                    recorder.Add(m.MemberAsString ?? "");
            }
        }, []);
        return recorder;
    }

    public sealed class CallRecorder(IsolatedKeyring keyring, DBusConnection connection) : IDisposable
    {
        private readonly List<string> _calls = [];

        /// <summary>
        /// Chamadas vistas até agora, depois de garantir que o monitor já recebeu tudo o que passou pelo barramento
        /// (manda um <c>Ping</c> ao serviço e espera vê-lo: o barramento entrega na ordem).
        /// </summary>
        public async Task<IReadOnlyList<string>> CallsAsync()
        {
            using var other = await keyring.ConnectAsync();
            var before = Snapshot().Count(c => c == "Ping");
            await other.CallMethodAsync(Build(other, ServicePath, "org.freedesktop.DBus.Peer", "Ping", null));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Snapshot().Count(c => c == "Ping") <= before)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("O monitor não viu o Ping.");
                await Task.Delay(20);
            }
            return Snapshot().Where(c => c != "Ping").ToArray();
        }

        private string[] Snapshot()
        {
            lock (_calls) return _calls.ToArray();
        }

        internal void Add(string member)
        {
            lock (_calls) _calls.Add(member);
        }

        public void Dispose() => connection.Dispose();
    }

    private static ProcessStartInfo IsolatedStart(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // Ambiente limpo: nada de DBUS_SESSION_BUS_ADDRESS, GNOME_KEYRING_CONTROL ou DISPLAY reais.
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        return start;
    }

    private Process Track(Process process)
    {
        _processes.Add(process);
        // Descarta o stderr para o processo nunca travar com o pipe cheio.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static string? FindBinary(string name) =>
        new[] { "/usr/bin", "/bin", "/usr/local/bin" }.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    public async ValueTask DisposeAsync()
    {
        foreach (var process in Enumerable.Reverse(_processes))
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException)
            {
                // Já saiu ou não respondeu; o diretório é apagado de todo modo.
            }
            process.Dispose();
        }
        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Algum socket ainda fechando; o sistema limpa o /tmp depois.
        }
    }
}
