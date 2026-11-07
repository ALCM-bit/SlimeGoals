using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Account;

namespace SlimeGoals.App.Tests.Account;

public class RecoveryCodeClipboardTests
{
    private const string Code = "ABCD-EFGH-IJKL-MNOP";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));
    private readonly FakeClipboard _clipboard = new();

    private sealed class FakeClipboard : ITextClipboard
    {
        public string? Text { get; set; }
        public int Clears { get; private set; }
        public bool FailReads { get; set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> TryGetTextAsync() =>
            FailReads ? Task.FromException<string?>(new InvalidOperationException("sem acesso")) : Task.FromResult(Text);

        public Task ClearAsync()
        {
            Clears++;
            Text = null;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Copies_and_clears_after_sixty_seconds_if_still_the_code()
    {
        var copy = new RecoveryCodeClipboard(_time).CopyAsync(_clipboard, Code);
        _clipboard.Text.Should().Be(Code);

        _time.Advance(TimeSpan.FromSeconds(59));
        _clipboard.Text.Should().Be(Code);
        copy.IsCompleted.Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        await copy;

        _clipboard.Text.Should().BeNull();
        _clipboard.Clears.Should().Be(1);
    }

    [Fact]
    public async Task Keeps_what_the_person_copied_afterwards()
    {
        var copy = new RecoveryCodeClipboard(_time).CopyAsync(_clipboard, Code);
        _clipboard.Text = "outra coisa";

        _time.Advance(RecoveryCodeClipboard.ClearAfter);
        await copy;

        _clipboard.Text.Should().Be("outra coisa");
        _clipboard.Clears.Should().Be(0);
    }

    [Fact]
    public async Task Unreadable_clipboard_is_left_alone_and_never_throws()
    {
        var copy = new RecoveryCodeClipboard(_time).CopyAsync(_clipboard, Code);
        _clipboard.FailReads = true;

        _time.Advance(RecoveryCodeClipboard.ClearAfter);
        await copy;

        _clipboard.Clears.Should().Be(0);
    }

    [Fact]
    public void Clears_after_sixty_seconds()
    {
        RecoveryCodeClipboard.ClearAfter.Should().Be(TimeSpan.FromSeconds(60));
    }
}
