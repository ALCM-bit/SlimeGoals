using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Support;
using SlimeGoals.Core.Domain;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Hosting;

public sealed class FocusQuitNoticeTests : IDisposable
{
    private readonly TimerFixture _f = new();
    private readonly List<string> _calls = [];
    private readonly FakeLifetime _inner;
    private Func<Task>? _notice;

    public FocusQuitNoticeTests() => _inner = new FakeLifetime(_calls);

    public void Dispose() => _f.Dispose();

    private FocusQuitNotice Create() => new(_inner, _f.Focus, (title, body, _) =>
    {
        _calls.Add($"{title} | {body}");
        return _notice?.Invoke() ?? Task.CompletedTask;
    }, _f.Time);

    [Fact]
    public async Task Quit_with_a_session_sends_the_notice()
    {
        await _f.Focus.StartAsync(FocusPlan.Default, null);
        await _f.Focus.PauseAsync();

        await Create().QuitAsync();

        _calls.Should().Equal(
            "Pomodoro guardado | A sessão continua guardada, mas o aviso de fim de bloco só toca com o SlimeGoals aberto.",
            "quit");
    }

    [Fact]
    public async Task Quit_with_a_finished_session_just_quits()
    {
        await _f.Focus.StartAsync(FocusPlan.Default, null);
        _f.Time.Advance(TimeSpan.FromMinutes(10));
        await _f.Focus.EndAsync(); // sem meta e com foco feito: fica guardada à espera de um destino
        (await _f.Focus.GetAsync())!.FinishedAt.Should().NotBeNull();

        await Create().QuitAsync();

        _calls.Should().Equal("quit");
    }

    [Fact]
    public async Task Quit_without_a_session_just_quits()
    {
        await Create().QuitAsync();

        _calls.Should().Equal("quit");
    }

    [Fact]
    public async Task A_failed_notice_still_quits_and_the_trace_has_no_message()
    {
        using var trace = new TraceCapture();
        await _f.Focus.StartAsync(FocusPlan.Default, null);
        _notice = () => Task.FromException(new InvalidOperationException("segredo"));

        await Create().QuitAsync();

        _calls.Should().EndWith("quit");
        trace.Messages.Should().Contain(m => m.Contains("InvalidOperationException"));
        trace.Messages.Should().NotContain(m => m.Contains("segredo"));
    }

    [Fact]
    public async Task A_notice_that_hangs_does_not_hold_the_quit()
    {
        await _f.Focus.StartAsync(FocusPlan.Default, null);
        _notice = () => new TaskCompletionSource().Task;

        var quit = Create().QuitAsync();
        await Task.Yield();
        _f.Time.Advance(FocusQuitNotice.NoticeTimeout);
        await quit;

        _calls.Should().EndWith("quit");
    }

    private sealed class FakeLifetime(List<string> calls) : IAppLifetime
    {
        public void Quit() => calls.Add("quit");
    }
}
