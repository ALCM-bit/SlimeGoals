using System.Net;
using System.Text.RegularExpressions;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Core.Tests.Diagnostics;

public partial class TraceTextTests
{
    private const string Secret = "segredo-que-nao-pode-ir-ao-log";

    [Fact]
    public void Of_names_every_exception_in_the_chain_without_their_messages()
    {
        var thrown = Throw(new InvalidOperationException(Secret, new FormatException(Secret)));

        var text = TraceText.Of(thrown);

        text.Should().StartWith("InvalidOperationException <- FormatException");
        text.Should().NotContain(Secret);
        text.Should().Contain(nameof(Throw), "a pilha diz onde falhou");
    }

    [Fact]
    public void Of_keeps_the_http_status()
    {
        var failure = new HttpRequestException(Secret, null, HttpStatusCode.BadGateway);

        TraceText.Of(failure).Should().Be("HttpRequestException (HTTP 502)");
    }

    [Fact]
    public void Of_lists_what_an_aggregate_carries()
    {
        var aggregate = new AggregateException(Secret, new TimeoutException(Secret), new IOException(Secret));

        var text = TraceText.Of(aggregate);

        text.Should().StartWith("AggregateException <- TimeoutException");
        text.Should().NotContain(Secret);
    }

    [Fact]
    public void Types_has_no_stack()
    {
        var thrown = Throw(new InvalidOperationException(Secret));

        TraceText.Types(thrown).Should().Be("InvalidOperationException");
    }

    /// <summary>
    /// O Trace do app vai para o journal do sistema, que fica gravado. Nenhuma chamada pode interpolar a exceção
    /// inteira nem a mensagem dela: é por onde títulos de metas, caminhos e respostas do servidor chegariam ao log.
    /// </summary>
    [Fact]
    public void No_trace_call_in_the_app_writes_the_exception_or_its_message()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(root, f), Line: i + 1, Text: line)))
            .Where(l => RawExceptionInTrace().IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();

        offenders.Should().BeEmpty("use TraceText.Of(e) ou TraceText.Types(e)");
    }

    [GeneratedRegex(@"Trace\.Trace\w+\(.*\{\s*(e|ex|exception|\w+\.InnerException)(\.Message|\.ToString\(\))?\s*\}")]
    private static partial Regex RawExceptionInTrace();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlimeGoals.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Não achei a raiz do repositório.");
    }

    private static Exception Throw(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
