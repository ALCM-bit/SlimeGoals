using System.Net.Sockets;
using SlimeGoals.Core.Abstractions;
using Tmds.DBus.Protocol;

namespace SlimeGoals.Platform.Linux.Secrets;

/// <summary>
/// <see cref="ISecretVault"/> sobre o Secret Service do freedesktop (gnome-keyring, KeePassXC...), direto pelo
/// Tmds.DBus.Protocol. Os itens vão para a coleção <c>default</c>, com os atributos <c>application=slimegoals</c>
/// e <c>name</c>. A sessão é <c>plain</c>: o segredo passa sem cifra extra pelo D-Bus local (ver o spike).
/// <para>
/// Cada operação pede uma conexão nova a <c>connect</c> e a descarta no fim, junto com a sessão. Coleção travada
/// chama <c>Unlock</c> e espera o <c>Prompt</c>; recusa, serviço ausente ou falha de D-Bus viram
/// <see cref="SecretVaultUnavailableException"/>. Nada é gravado em arquivo como alternativa (§6).
/// </para>
/// </summary>
public sealed class SecretServiceVault(Func<Task<DBusConnection>> connect) : ISecretVault
{
    private const string Service = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string CollectionInterface = "org.freedesktop.Secret.Collection";
    private const string ItemInterface = "org.freedesktop.Secret.Item";
    private const string SessionInterface = "org.freedesktop.Secret.Session";
    private const string PromptInterface = "org.freedesktop.Secret.Prompt";
    private const string NoObject = "/";

    private const string Application = "slimegoals";
    private const string ItemLabel = "SlimeGoals: sincronização";
    private const string DefaultCollectionLabel = "Padrão";
    private const string ContentType = "application/octet-stream";

    /// <summary>Cofre no barramento da sessão do usuário (<c>DBUS_SESSION_BUS_ADDRESS</c>).</summary>
    public static SecretServiceVault ForSessionBus() => new(async () =>
    {
        var address = DBusAddress.Session
                      ?? throw new SecretVaultUnavailableException("Não há barramento de sessão D-Bus.");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    });

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // Abrir a sessão basta: confirma que o serviço responde, sem pedir desbloqueio.
            return await RunAsync(_ => Task.FromResult(true), ct).ConfigureAwait(false);
        }
        catch (SecretVaultUnavailableException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<byte[]?> GetAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return RunAsync(async s =>
        {
            if (await s.DefaultCollectionAsync(create: false).ConfigureAwait(false) is not { } collection) return null;
            await s.UnlockAsync(collection).ConfigureAwait(false);
            var items = await s.SearchAsync(collection, name).ConfigureAwait(false);
            return items.Length == 0 ? null : await s.GetSecretAsync(items[0]).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task SetAsync(string name, byte[] secret, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(secret);
        return RunAsync(async s =>
        {
            var collection = (await s.DefaultCollectionAsync(create: true).ConfigureAwait(false))!;
            await s.UnlockAsync(collection).ConfigureAwait(false);
            await s.CreateItemAsync(collection, name, secret).ConfigureAwait(false);
            return true;
        }, ct);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return RunAsync(async s =>
        {
            if (await s.DefaultCollectionAsync(create: false).ConfigureAwait(false) is not { } collection) return true;
            await s.UnlockAsync(collection).ConfigureAwait(false);
            foreach (var item in await s.SearchAsync(collection, name).ConfigureAwait(false))
                await s.DeleteItemAsync(item).ConfigureAwait(false);
            return true;
        }, ct);
    }

    /// <summary>Conecta, abre a sessão, executa e fecha tudo. Falhas de D-Bus viram cofre indisponível.</summary>
    private async Task<T> RunAsync<T>(Func<Session, Task<T>> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DBusConnection? connection = null;
        try
        {
            connection = await connect().ConfigureAwait(false);
            var session = await Session.OpenAsync(connection, ct).ConfigureAwait(false);
            try
            {
                return await operation(session).ConfigureAwait(false);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is DBusExceptionBase or SocketException or IOException)
        {
            throw new SecretVaultUnavailableException($"Cofre de segredos indisponível: {e.Message}", e);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private delegate void BodyWriter(ref MessageWriter writer);

    /// <summary>Monta a chamada no mesmo método, com o <see cref="MessageWriter"/> por <c>ref</c> (armadilha do spike).</summary>
    private static MessageBuffer Build(DBusConnection connection, string path, string @interface, string member,
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

    private static void WriteAttributes(ref MessageWriter w, string name)
    {
        var dict = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart();
        w.WriteString("application");
        w.WriteString(Application);
        w.WriteDictionaryEntryStart();
        w.WriteString("name");
        w.WriteString(name);
        w.WriteDictionaryEnd(dict);
    }

    /// <summary>Uma sessão <c>plain</c> aberta numa conexão; as operações do Secret Service usam ambas.</summary>
    private sealed class Session(DBusConnection connection, string path, CancellationToken ct)
    {
        public static async Task<Session> OpenAsync(DBusConnection connection, CancellationToken ct)
        {
            var message = Build(connection, ServicePath, ServiceInterface, "OpenSession", "sv", (ref MessageWriter w) =>
            {
                w.WriteString("plain");
                w.WriteVariantString("");
            });
            var path = await connection.CallMethodAsync(message, (m, _) =>
            {
                var r = m.GetBodyReader();
                r.ReadVariantValue();
                return r.ReadObjectPathAsString();
            }, null).ConfigureAwait(false);
            return new Session(connection, path, ct);
        }

        /// <summary>
        /// Caminho da coleção <c>default</c>. Sem ela, devolve <c>null</c>, ou, com <paramref name="create"/>, cria
        /// uma com esse alias (o serviço costuma pedir a senha nova num prompt).
        /// </summary>
        public async Task<string?> DefaultCollectionAsync(bool create)
        {
            var alias = await ReadDefaultAliasAsync().ConfigureAwait(false);
            if (alias != NoObject || !create) return alias == NoObject ? null : alias;

            var message = Build(connection, ServicePath, ServiceInterface, "CreateCollection", "a{sv}s",
                (ref MessageWriter w) =>
                {
                    var dict = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("org.freedesktop.Secret.Collection.Label");
                    w.WriteVariantString(DefaultCollectionLabel);
                    w.WriteDictionaryEnd(dict);
                    w.WriteString("default");
                });
            var (collection, prompt) = await CallAsync(message, ReadPathAndPrompt).ConfigureAwait(false);
            if (collection != NoObject) return collection;
            await PromptAsync(prompt).ConfigureAwait(false);
            alias = await ReadDefaultAliasAsync().ConfigureAwait(false);
            return alias != NoObject
                ? alias
                : throw new SecretVaultUnavailableException("O cofre não criou a coleção padrão.");
        }

        private Task<string> ReadDefaultAliasAsync() =>
            CallAsync(Build(connection, ServicePath, ServiceInterface, "ReadAlias", "s",
                (ref MessageWriter w) => w.WriteString("default")), (m, _) => m.GetBodyReader().ReadObjectPathAsString());

        /// <summary>Destrava a coleção; se o serviço devolver um prompt, mostra e espera a resposta da pessoa.</summary>
        public async Task UnlockAsync(string collection)
        {
            var message = Build(connection, ServicePath, ServiceInterface, "Unlock", "ao",
                (ref MessageWriter w) => w.WriteArray(new ObjectPath[] { new(collection) }));
            var prompt = await CallAsync(message, (m, _) =>
            {
                var r = m.GetBodyReader();
                r.ReadArrayOfObjectPath();
                return r.ReadObjectPathAsString();
            }).ConfigureAwait(false);
            if (prompt != NoObject) await PromptAsync(prompt).ConfigureAwait(false);
        }

        public Task<ObjectPath[]> SearchAsync(string collection, string name) =>
            CallAsync(Build(connection, collection, CollectionInterface, "SearchItems", "a{ss}",
                (ref MessageWriter w) => WriteAttributes(ref w, name)), (m, _) => m.GetBodyReader().ReadArrayOfObjectPath());

        public Task<byte[]> GetSecretAsync(ObjectPath item) =>
            CallAsync(Build(connection, item.ToString(), ItemInterface, "GetSecret", "o",
                (ref MessageWriter w) => w.WriteObjectPath(path)), (m, _) =>
            {
                var r = m.GetBodyReader();
                r.AlignStruct();
                r.ReadObjectPath();
                r.ReadArrayOfByte();
                var value = r.ReadArrayOfByte();
                r.ReadString();
                return value;
            });

        public async Task CreateItemAsync(string collection, string name, byte[] secret)
        {
            var message = Build(connection, collection, CollectionInterface, "CreateItem", "a{sv}(oayays)b",
                (ref MessageWriter w) =>
                {
                    var properties = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("org.freedesktop.Secret.Item.Label");
                    w.WriteVariantString(ItemLabel);
                    w.WriteDictionaryEntryStart();
                    w.WriteString("org.freedesktop.Secret.Item.Attributes");
                    w.WriteSignature("a{ss}");
                    WriteAttributes(ref w, name);
                    w.WriteDictionaryEnd(properties);

                    w.WriteStructureStart();
                    w.WriteObjectPath(path);
                    w.WriteArray(ReadOnlySpan<byte>.Empty);
                    w.WriteArray(secret);
                    w.WriteString(ContentType);

                    w.WriteBool(true); // substitui o item com os mesmos atributos
                });
            var (_, prompt) = await CallAsync(message, ReadPathAndPrompt).ConfigureAwait(false);
            if (prompt != NoObject) await PromptAsync(prompt).ConfigureAwait(false);
        }

        public async Task DeleteItemAsync(ObjectPath item)
        {
            var prompt = await CallAsync(Build(connection, item.ToString(), ItemInterface, "Delete", null),
                (m, _) => m.GetBodyReader().ReadObjectPathAsString()).ConfigureAwait(false);
            if (prompt != NoObject) await PromptAsync(prompt).ConfigureAwait(false);
        }

        /// <summary>
        /// Mostra o prompt e espera o sinal <c>Completed</c>. O sinal é observado antes de chamar <c>Prompt</c>, para
        /// não perder uma resposta imediata. Recusa vira <see cref="SecretVaultUnavailableException"/>; cancelar
        /// dispensa o prompt.
        /// </summary>
        private async Task PromptAsync(string prompt)
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = await connection.WatchSignalAsync(Service, prompt, PromptInterface, "Completed",
                (m, _) => m.GetBodyReader().ReadBool(),
                n =>
                {
                    if (n.HasValue) completed.TrySetResult(n.Value);
                    else if (n.IsCompletion) completed.TrySetException(n.CompletionReason);
                },
                ObserverFlags.EmitOnConnectionClosed | ObserverFlags.EmitOnOwnerChanged, false, null)
                .ConfigureAwait(false);
            await CallAsync(Build(connection, prompt, PromptInterface, "Prompt", "s",
                (ref MessageWriter w) => w.WriteString(""))).ConfigureAwait(false);

            bool dismissed;
            await using (ct.Register(() => completed.TrySetCanceled(ct)).ConfigureAwait(false))
            {
                try
                {
                    dismissed = await completed.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    connection.TrySendMessage(Build(connection, prompt, PromptInterface, "Dismiss", null));
                    throw;
                }
            }
            if (dismissed) throw new SecretVaultUnavailableException("O desbloqueio do cofre de segredos foi recusado.");
        }

        /// <summary>Fecha a sessão no serviço, sem deixar uma falha aqui esconder a da operação.</summary>
        public async Task CloseAsync()
        {
            try
            {
                await connection.CallMethodAsync(Build(connection, path, SessionInterface, "Close", null))
                    .ConfigureAwait(false);
            }
            catch (DBusExceptionBase)
            {
                // A sessão morre com a conexão de todo modo.
            }
        }

        private async Task<T> CallAsync<T>(MessageBuffer message, MessageValueReader<T> read)
        {
            ct.ThrowIfCancellationRequested();
            return await connection.CallMethodAsync(message, read, null).ConfigureAwait(false);
        }

        private async Task CallAsync(MessageBuffer message)
        {
            ct.ThrowIfCancellationRequested();
            await connection.CallMethodAsync(message).ConfigureAwait(false);
        }

        private static (string Path, string Prompt) ReadPathAndPrompt(Message m, object? _)
        {
            var r = m.GetBodyReader();
            return (r.ReadObjectPathAsString(), r.ReadObjectPathAsString());
        }
    }
}
