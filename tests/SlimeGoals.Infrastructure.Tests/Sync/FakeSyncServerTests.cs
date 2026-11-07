using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Core.Sync;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Sync;

public class FakeSyncServerTests
{
    private static readonly byte[] Blob = [1, 2, 3];

    private readonly FakeSyncServer _server = new();
    private readonly PlainSyncCrypto _crypto = new();

    private static SyncEnvelope Envelope(Guid id, int version = SyncCodec.FormatVersion) =>
        new(version, SyncRecordKind.Goal, id, Guid.NewGuid(), "{}");

    [Fact]
    public async Task Rejected_items_do_not_use_a_seq_and_the_rest_is_accepted()
    {
        var result = await _server.PushAsync([new("a", 0, Blob), new("b", 9, Blob), new("c", 0, Blob)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1), new PushAccepted("c", 2));
        result.Conflicts.Should().Equal("b");
    }

    [Fact]
    public async Task Resending_an_accepted_item_is_a_conflict()
    {
        await _server.PushAsync([new("a", 0, Blob)]);

        var result = await _server.PushAsync([new("a", 0, Blob)]);

        result.Conflicts.Should().Equal("a");
        _server.Records.Should().ContainSingle().Which.Seq.Should().Be(1);
    }

    [Fact]
    public async Task Deletion_clears_the_blob_and_raises_the_seq()
    {
        await _server.PushAsync([new("a", 0, Blob)]);

        await _server.PushAsync([new("a", 1, null)]);

        var record = _server.Records.Should().ContainSingle().Subject;
        record.Blob.Should().BeNull();
        record.Seq.Should().Be(2);
    }

    [Fact]
    public async Task Deletion_of_an_unknown_key_is_accepted()
    {
        var result = await _server.PushAsync([new("a", 0, null)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1));
    }

    [Fact]
    public async Task Pull_pages_in_order()
    {
        await _server.PushAsync([new("a", 0, Blob), new("b", 0, Blob), new("c", 0, Blob)]);

        var first = await _server.PullAsync(0, 2, full: false);
        var second = await _server.PullAsync(2, 2, full: false);

        first.Records.Select(r => r.RecordKey).Should().Equal("a", "b");
        first.HasMore.Should().BeTrue();
        second.Records.Select(r => r.RecordKey).Should().Equal("c");
        second.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Pull_behind_the_purge_requires_reset_even_from_zero()
    {
        await _server.PushAsync([new("a", 0, null), new("b", 0, Blob)]);
        _server.PurgeBefore(2);

        var behind = () => _server.PullAsync(1, 10, full: false);
        var fromZero = () => _server.PullAsync(0, 10, full: false);

        await behind.Should().ThrowAsync<SyncResetRequiredException>();
        await fromZero.Should().ThrowAsync<SyncResetRequiredException>();
        (await _server.PullAsync(2, 10, full: false)).Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_never_passes_the_highest_seq()
    {
        await _server.PushAsync([new("a", 0, null), new("b", 0, null)]);

        _server.PurgeBefore(long.MaxValue);

        _server.Records.Should().ContainSingle().Which.Should().Be(new RemoteRecord("b", 2, null));
        (await _server.PullAsync(2, 10, full: false)).Records.Should().BeEmpty();
        var behind = () => _server.PullAsync(1, 10, full: false);
        await behind.Should().ThrowAsync<SyncResetRequiredException>();
    }

    [Fact]
    public async Task Purge_of_an_empty_account_requires_no_reset()
    {
        _server.PurgeBefore(10);

        (await _server.PullAsync(0, 10, full: false)).Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Server_without_purge_never_requires_reset()
    {
        await _server.PushAsync([new("a", 0, Blob)]);

        var page = await _server.PullAsync(0, 10, full: false);

        page.Records.Select(r => r.RecordKey).Should().Equal("a");
    }

    [Fact]
    public async Task Full_pull_never_requires_reset_and_is_recorded()
    {
        await _server.PushAsync([new("a", 0, Blob), new("b", 0, Blob), new("c", 0, Blob)]);
        _server.PurgeBefore(3);

        var page = await _server.PullAsync(1, 10, full: true);

        page.Records.Select(r => r.RecordKey).Should().Equal("b", "c");
        _server.PullRequests.Should().Equal(new FakeSyncServer.PullRequest(1, true));
    }

    [Fact]
    public async Task Lost_ack_applies_and_then_throws()
    {
        _server.LoseNextAck();

        var act = () => _server.PushAsync([new("a", 0, Blob)]);

        await act.Should().ThrowAsync<IOException>();
        _server.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task Failure_applies_nothing_and_happens_once()
    {
        _server.FailNext(new TimeoutException());

        var act = () => _server.PushAsync([new("a", 0, Blob)]);

        await act.Should().ThrowAsync<TimeoutException>();
        _server.Records.Should().BeEmpty();
        (await _server.PullAsync(0, 10, full: false)).Records.Should().BeEmpty();
    }

    [Fact]
    public void Crypto_round_trips_the_envelope()
    {
        var envelope = Envelope(Guid.NewGuid());

        _crypto.Open(_crypto.RecordKey(envelope.RecordId), _crypto.Seal(envelope)).Should().Be(envelope);
    }

    [Fact]
    public void Crypto_refuses_the_wrong_key()
    {
        var blob = _crypto.Seal(Envelope(Guid.NewGuid()));

        var act = () => _crypto.Open(_crypto.RecordKey(Guid.NewGuid()), blob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Crypto_refuses_corrupted_content()
    {
        var act = () => _crypto.Open("k:x", Encoding.UTF8.GetBytes("{quebrado"));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Crypto_refuses_another_version()
    {
        var id = Guid.NewGuid();
        var blob = _crypto.Seal(Envelope(id, SyncCodec.FormatVersion + 1));

        var act = () => _crypto.Open(_crypto.RecordKey(id), blob);

        act.Should().Throw<SyncFormatException>().Which.Version.Should().Be(SyncCodec.FormatVersion + 1);
    }
}
