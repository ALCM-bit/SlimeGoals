using System.Net;
using System.Text;
using Dapper;
using Npgsql;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Tests.Sync;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Testing;

namespace SlimeGoals.Server.Tests.Account;

/// <summary>
/// O caminho inteiro da 9.4: serviço de conta, cofre, cripto de verdade e o motor do app, contra o servidor e o
/// PostgreSQL. Cada aparelho tem o seu banco SQLite, o seu cofre e o seu estado, que o serviço de conta liga e desliga.
/// </summary>
public sealed class EncryptedSyncEndToEndTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int MaxRounds = 30;
    private const string Password = "senha Pwqzx ação longa";
    private const string NewPassword = "outra senha Mnbvc maçã";

    // Valores com acento e um pedaço ASCII próprio: o JSON do registro escapa o acento (á vira á), então só
    // procurar "água" em UTF-8 nunca falharia. O pedaço ASCII aparece igual em qualquer forma legível.
    private const string Title = "Beber água Zyxwq";
    private const string Unit = "copos Qvlrt";
    private const string Note = "nota Kjhgf ção";

    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ServerFactory _factory = new(postgres);
    private readonly List<Phone> _phones = [];
    private readonly List<Phone> _created = [];
    private readonly string _email = $"user{Guid.NewGuid():N}@exemplo.com";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var phone in _created)
            phone.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Two_devices_sync_encrypted_through_the_real_server()
    {
        var a = NewPhone();
        await a.RegisterAsync(_email, Password);
        var reading = await a.Sim.CreateGoal("Ler");
        await a.Sim.Adjust(reading.Id, 2, "capítulo um");

        var b = NewPhone();
        await b.Service.LoginAsync(_email, Password, "celular");
        await b.OpenAsync();
        await SettleAsync();

        // As duas pontas criam e registram; cada uma tem de ver o que a outra fez.
        var running = await b.Sim.CreateGoal("Correr");
        await b.Sim.Adjust(running.Id, 1);
        await b.Sim.Adjust(reading.Id, 3);
        a.Sim.Time.Advance(TimeSpan.FromMinutes(1));
        await a.Sim.EditGoal(reading.Id, "Ler mais");
        await SettleAsync();

        ShouldBeEqual();
        a.Sim.Snapshot().Goals.Select(g => g.Title).Should().BeEquivalentTo("Ler mais", "Correr");
        a.Sim.Snapshot().Checkins.Should().HaveCount(3);
        b.Sim.Snapshot().Checkins.Should().Contain(c => c.Row.Contains("capítulo um", StringComparison.Ordinal));

        // O que o servidor guarda é o blob cifrado: versão 1 seguida de nonce, cifra e tag.
        foreach (var blob in await BlobsAsync())
        {
            blob[0].Should().Be(1);
            blob.Length.Should().BeGreaterThan(1 + 12 + 16);
        }
    }

    [Fact]
    public async Task Database_dump_has_no_readable_content()
    {
        var a = NewPhone();
        var recoveryCode = await a.RegisterAsync(_email, Password);
        var goal = await a.Sim.CreateGoal(Title);
        goal = await a.Sim.EditGoal(goal.Id, Title, d => d with { Unit = Unit });
        await a.Sim.Adjust(goal.Id, 1, Note);

        var b = NewPhone();
        await b.Service.LoginAsync(_email, Password, "celular");
        await b.OpenAsync();
        await SettleAsync();

        // Sem isto, um dump limpo poderia ser só um dado que nunca chegou ao servidor.
        ShouldBeEqual();
        b.Sim.Snapshot().Goals.Should().ContainSingle(g => g.Title == Title && g.Row.Contains(Unit, StringComparison.Ordinal));
        b.Sim.Snapshot().Checkins.Should().ContainSingle(c => c.Row.Contains(Note, StringComparison.Ordinal));
        (await BlobsAsync()).Should().HaveCount(2);

        var dump = await DumpAsync();
        dump.Should().NotBeEmpty();
        var secrets = new[]
        {
            Title, Unit, Note, Password,
            recoveryCode, recoveryCode.Replace("-", "", StringComparison.Ordinal),
        };
        var fragments = new[] { "Zyxwq", "Qvlrt", "Kjhgf", "Pwqzx", "água", "ção" };

        var found = new List<string>();
        foreach (var (column, value) in dump)
        {
            foreach (var (label, needle) in secrets.SelectMany(Forms).Concat(fragments.SelectMany(Forms)))
            {
                if (value.AsSpan().IndexOf(needle) >= 0)
                    found.Add($"{label} em {column}");
            }
        }

        string.Join("\n", found).Should().BeEmpty("o servidor não guarda nada legível (§9 Privacidade)");
    }

    [Fact]
    public async Task Device_revoked_by_password_change_signs_out_and_back_in_without_duplicates()
    {
        var a = NewPhone();
        await a.RegisterAsync(_email, Password);
        var reading = await a.Sim.CreateGoal("Ler");
        await a.Sim.Adjust(reading.Id, 2);

        var b = NewPhone();
        await b.Service.LoginAsync(_email, Password, "celular");
        await b.OpenAsync();
        await b.Sim.CreateGoal("Correr");
        await SettleAsync();

        await a.Service.ChangePasswordAsync(Password, NewPassword);

        // O aparelho revogado continua usando o app e registra sem saber.
        b.Sim.Time.Advance(TimeSpan.FromMinutes(1));
        await b.Sim.Adjust(reading.Id, 4);
        b.Handler.Calls.Clear();
        (await b.Sim.SyncAsync()).Should().Be(SyncCycleResult.Unauthorized);
        b.Handler.Calls.Should().Contain(c => c.Path.StartsWith("/sync/", StringComparison.Ordinal)
            && c.Status == HttpStatusCode.Unauthorized);

        await b.SignOutAsync();
        (await b.Sim.State.GetAsync()).Enabled.Should().BeFalse();
        b.Vault.Names.Should().BeEmpty();

        await b.Service.LoginAsync(_email, NewPassword, "celular");
        await b.OpenAsync();
        b.Handler.Calls.Clear();
        await SettleAsync();

        // Saiu e entrou: baixa tudo desde o começo, e o registro feito revogado sobe junto, uma vez só.
        b.Handler.Calls.Should().Contain(c => c.Path == "/sync/pull" && c.Query.Contains("after=0", StringComparison.Ordinal));
        ShouldBeEqual();
        foreach (var phone in _phones)
        {
            var snapshot = phone.Sim.Snapshot();
            snapshot.Goals.Select(g => g.Title).Should().BeEquivalentTo("Ler", "Correr");
            snapshot.Checkins.Select(c => c.Amount).Should().BeEquivalentTo([2, 4]);
            snapshot.Goals.Select(g => g.Id).Should().OnlyHaveUniqueItems();
            snapshot.Checkins.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        }
        (await BlobsAsync()).Should().HaveCount(4, "duas metas e dois registros, sem cópia no servidor");
    }

    [Fact]
    public async Task Recovered_device_reads_everything()
    {
        var a = NewPhone();
        var recoveryCode = await a.RegisterAsync(_email, Password);
        var reading = await a.Sim.CreateGoal(Title);
        await a.Sim.Adjust(reading.Id, 2, Note);
        await a.Sim.CreateGoal("Correr");
        await SettleAsync();
        var expected = a.Sim.Snapshot().Lines;

        var c = NewPhone();
        await c.Service.RecoverAsync(_email, recoveryCode.ToLowerInvariant(), NewPassword, "notebook");
        await c.OpenAsync();
        _phones.Remove(a); // a recuperação revogou os outros aparelhos; ele sai do rodízio
        await SettleAsync();

        c.Sim.Snapshot().Lines.Should().Equal(expected);
        c.Sim.LastError.Should().BeNull();
        (await a.Sim.SyncAsync()).Should().Be(SyncCycleResult.Unauthorized, "a recuperação revoga os outros aparelhos");
    }

    private Phone NewPhone()
    {
        var phone = new Phone(_factory, _phones.Count + 1);
        _phones.Add(phone);
        _created.Add(phone);
        return phone;
    }

    private async Task<NpgsqlConnection> OpenDatabaseAsync()
    {
        var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<List<byte[]>> BlobsAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<byte[]>("SELECT blob FROM records WHERE blob IS NOT NULL")).ToList();
    }

    /// <summary>
    /// Todas as colunas de texto e binárias de <c>accounts</c>, <c>devices</c> e <c>records</c>, achadas pelo
    /// <c>information_schema</c>: coluna nova entra sozinha. Texto vira UTF-8; binário vai como está.
    /// </summary>
    private async Task<List<(string Column, byte[] Value)>> DumpAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        var columns = (await connection.QueryAsync<(string Table, string Column, string Type)>("""
            SELECT table_name, column_name, data_type FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('accounts', 'devices', 'records')
              AND data_type IN ('text', 'character varying', 'character', 'bytea', 'json', 'jsonb')
            """)).ToList();
        columns.Select(c => c.Table).Distinct().Should().HaveCount(3);
        columns.Should().Contain(c => c.Table == "records" && c.Column == "blob");

        var dump = new List<(string, byte[])>();
        foreach (var (table, column, type) in columns)
        {
            var sql = type == "bytea" ? $"SELECT \"{column}\" FROM \"{table}\"" : $"SELECT \"{column}\"::text FROM \"{table}\"";
            if (type == "bytea")
            {
                foreach (var value in await connection.QueryAsync<byte[]?>(sql))
                    if (value is not null)
                        dump.Add(($"{table}.{column}", value));
            }
            else
            {
                foreach (var value in await connection.QueryAsync<string?>(sql))
                    if (value is not null)
                        dump.Add(($"{table}.{column}", Encoding.UTF8.GetBytes(value)));
            }
        }

        return dump;
    }

    /// <summary>As formas em que um valor legível poderia aparecer: UTF-8, UTF-16LE, JSON escapado e base64.</summary>
    private static IEnumerable<(string Label, byte[] Needle)> Forms(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        yield return ($"\"{value}\" em UTF-8", utf8);
        yield return ($"\"{value}\" em UTF-16", Encoding.Unicode.GetBytes(value));
        foreach (var upper in new[] { true, false })
        {
            var escaped = JsonEscaped(value, upper);
            if (escaped != value)
                yield return ($"\"{value}\" escapado como {escaped}", Encoding.ASCII.GetBytes(escaped));
        }

        // Base64 depende de onde o valor começa: as três posições, sem os grupos das pontas, que mudam com o vizinho.
        foreach (var bytes in new[] { utf8, Encoding.Unicode.GetBytes(value) })
        {
            for (var shift = 0; shift < 3; shift++)
            {
                var text = Convert.ToBase64String([.. new byte[shift], .. bytes]);
                var core = shift == 0 ? text[..^4] : text[4..^4];
                if (core.Length >= 8)
                    yield return ($"\"{value}\" em base64", Encoding.ASCII.GetBytes(core));
            }
        }
    }

    private static string JsonEscaped(string value, bool upper)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch < 0x80)
                builder.Append(ch);
            else
                builder.Append(upper ? $"\\u{(int)ch:X4}" : $"\\u{(int)ch:x4}");
        }
        return builder.ToString();
    }

    private async Task<long> LatestAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT next_seq - 1 FROM accounts WHERE email = @Email", new { Email = _email });
    }

    /// <summary>Ciclos em todos, em rodízio, até uma rodada não mudar nada. Não assentar é defeito.</summary>
    private async Task SettleAsync()
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var results = new List<SyncCycleResult>();
            foreach (var phone in _phones)
                results.Add(await phone.Sim.SyncAsync());

            var latest = await LatestAsync();
            if (results.All(r => r == SyncCycleResult.Done)
                && _phones.All(p => p.Sim.Pending == 0 && p.Sim.LastSeq >= latest && !p.Sim.FullResync))
                return;
        }

        var state = _phones.Select(p => $"{p.Sim.Name}: seq {p.Sim.LastSeq}, fila [{p.Sim.Queue}], erro {p.Sim.LastError ?? "nenhum"}");
        throw new InvalidOperationException($"Não assentou em {MaxRounds} rodadas.\n{string.Join("\n", state)}");
    }

    private void ShouldBeEqual()
    {
        var expected = _phones[0].Sim.Snapshot().Lines;
        foreach (var phone in _phones.Skip(1))
            phone.Sim.Snapshot().Lines.Should().Equal(expected, $"{phone.Sim.Name} tem de terminar igual a {_phones[0].Sim.Name}");
    }

    /// <summary>
    /// Um aparelho com conta: o motor do <see cref="SimulatedDevice"/> recebe a cripto e o token da sessão aberta do
    /// cofre, e o serviço de conta liga e desliga o mesmo estado que o motor lê.
    /// </summary>
    private sealed class Phone : IDisposable
    {
        private readonly ServerFactory _factory;
        private SyncSession? _session;

        public Phone(ServerFactory factory, int number)
        {
            _factory = factory;
            var client = factory.CreateDefaultClient(Handler);
            var transport = new HttpSyncTransport(client, ct => _session is { } s ? s.Token(ct) : ValueTask.FromResult<string?>(null));
            Sim = new SimulatedDevice($"aparelho {number}", Guid.Parse($"00000000-0000-0000-0000-{number:x12}"), transport, T0,
                new SessionCrypto(this));
            Service = new AccountService(new HttpAccountApi(client), Vault, Sim.State, client.BaseAddress!);
        }

        public SimulatedDevice Sim { get; }
        public InMemorySecretVault Vault { get; } = new();
        public CallRecorder Handler { get; } = new();
        public AccountService Service { get; }

        public ISyncCrypto Crypto => (_session ?? throw new InvalidOperationException("Sem sessão aberta.")).Crypto;

        /// <summary>Cadastra, confirma e abre a sessão; devolve o código de recuperação.</summary>
        public async Task<string> RegisterAsync(string email, string password)
        {
            using var pending = await Service.RegisterAsync(email, password);
            await Service.ConfirmAsync(pending, _factory.Email.LastToken, "pc");
            await OpenAsync();
            return pending.RecoveryCode;
        }

        public async Task OpenAsync()
        {
            _session?.Dispose();
            _session = await Service.OpenSessionAsync() ?? throw new InvalidOperationException("O cofre ficou sem sessão.");
        }

        public async Task SignOutAsync()
        {
            await Service.SignOutAsync();
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
            Sim.Dispose();
        }
    }

    /// <summary>A cripto da sessão do momento: o motor nasce antes da entrada na conta.</summary>
    private sealed class SessionCrypto(Phone phone) : ISyncCrypto
    {
        public string RecordKey(Guid recordId) => phone.Crypto.RecordKey(recordId);

        public byte[] Seal(SyncEnvelope envelope) => phone.Crypto.Seal(envelope);

        public SyncEnvelope Open(string recordKey, byte[] blob) => phone.Crypto.Open(recordKey, blob);
    }

    private sealed record Call(string Path, string Query, HttpStatusCode Status);

    /// <summary>Tudo que o servidor respondeu a um aparelho, na ordem.</summary>
    private sealed class CallRecorder : DelegatingHandler
    {
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            Calls.Add(new Call(request.RequestUri!.AbsolutePath, request.RequestUri.Query, response.StatusCode));
            return response;
        }
    }
}
