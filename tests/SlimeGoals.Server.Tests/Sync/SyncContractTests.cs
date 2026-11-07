using SlimeGoals.Core.Sync;

namespace SlimeGoals.Server.Tests.Sync;

/// <summary>
/// O que o motor espera de um servidor (§5), rodado contra o falso e contra o real. Divergência entre os dois é defeito.
/// </summary>
public abstract class SyncContractTests
{
    private static readonly byte[] Blob = [1, 2, 3];
    private static readonly byte[] OtherBlob = [4, 5, 6];

    /// <summary>Um transporte de uma conta nova, sem registros.</summary>
    protected abstract Task<ISyncTransport> NewAccountAsync();

    private static PushItem New(string key) => new(key, 0, Blob);

    private static Task<SyncPullPage> PullAllAsync(ISyncTransport account) => account.PullAsync(0, 100, false);

    [Fact]
    public async Task Pull_of_empty_account_is_empty()
    {
        var account = await NewAccountAsync();

        var page = await PullAllAsync(account);

        page.Records.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Accepted_items_get_increasing_seq_without_gaps()
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([New("a"), New("b"), New("c")]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1), new PushAccepted("b", 2), new PushAccepted("c", 3));
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Pull_returns_the_blob_that_was_pushed()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a")]);

        var record = (await PullAllAsync(account)).Records.Should().ContainSingle().Subject;

        record.RecordKey.Should().Be("a");
        record.Seq.Should().Be(1);
        record.Blob.Should().Equal(Blob);
    }

    [Fact]
    public async Task Empty_blob_is_a_live_record()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([new PushItem("a", 0, [])]);

        var record = (await PullAllAsync(account)).Records.Should().ContainSingle().Subject;

        record.Blob.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Push_of_empty_batch_answers_empty()
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([]);

        result.Accepted.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Pull_pages_in_seq_order()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a"), New("b"), New("c"), New("d"), New("e")]);

        var first = await account.PullAsync(0, 2, false);
        var second = await account.PullAsync(first.Records[^1].Seq, 2, false);
        var third = await account.PullAsync(second.Records[^1].Seq, 2, false);

        first.Records.Select(r => r.Seq).Should().Equal(1, 2);
        first.HasMore.Should().BeTrue();
        second.Records.Select(r => r.Seq).Should().Equal(3, 4);
        second.HasMore.Should().BeTrue();
        third.Records.Select(r => r.Seq).Should().Equal(5);
        third.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Page_that_ends_exactly_at_the_last_record_has_no_more()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a"), New("b")]);

        var page = await account.PullAsync(0, 2, false);

        page.Records.Should().HaveCount(2);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Stale_base_is_a_conflict_and_the_rest_is_accepted()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a")]);

        var result = await account.PushAsync([new PushItem("b", 0, Blob), new PushItem("a", 7, OtherBlob), new PushItem("c", 0, Blob)]);

        result.Conflicts.Should().Equal("a");
        // O item recusado não gasta seq.
        result.Accepted.Should().Equal(new PushAccepted("b", 2), new PushAccepted("c", 3));
        (await PullAllAsync(account)).Records.Single(r => r.RecordKey == "a").Blob.Should().Equal(Blob);
    }

    [Fact]
    public async Task Repeated_send_after_lost_ack_is_a_conflict_and_does_not_duplicate()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a")]);

        var repeated = await account.PushAsync([New("a")]);

        repeated.Accepted.Should().BeEmpty();
        repeated.Conflicts.Should().Equal("a");
        (await PullAllAsync(account)).Records.Should().ContainSingle().Which.Seq.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absent_key_with_base_above_zero_is_a_conflict(bool delete)
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([new PushItem("a", 3, delete ? null : Blob)]);

        result.Accepted.Should().BeEmpty();
        result.Conflicts.Should().Equal("a");
        (await PullAllAsync(account)).Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_with_base_zero_of_absent_key_creates_a_mark()
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([new PushItem("a", 0, null)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1));
        var record = (await PullAllAsync(account)).Records.Should().ContainSingle().Subject;
        record.Seq.Should().Be(1);
        record.Blob.Should().BeNull();
    }

    [Fact]
    public async Task Delete_keeps_a_mark_with_a_new_seq_and_no_blob()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a")]);

        var result = await account.PushAsync([new PushItem("a", 1, null)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 2));
        var record = (await PullAllAsync(account)).Records.Should().ContainSingle().Subject;
        record.Seq.Should().Be(2);
        record.Blob.Should().BeNull();
    }

    [Fact]
    public async Task Edit_raises_the_seq_and_the_old_seq_no_longer_appears()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a"), New("b")]);

        var result = await account.PushAsync([new PushItem("a", 1, OtherBlob)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 3));
        var records = (await PullAllAsync(account)).Records;
        records.Select(r => r.Seq).Should().Equal(2, 3);
        records[^1].Blob.Should().Equal(OtherBlob);
    }

    [Fact]
    public async Task Push_over_a_mark_with_its_seq_brings_the_record_back()
    {
        var account = await NewAccountAsync();
        await account.PushAsync([New("a")]);
        await account.PushAsync([new PushItem("a", 1, null)]);

        var stale = await account.PushAsync([new PushItem("a", 0, OtherBlob)]);
        var result = await account.PushAsync([new PushItem("a", 2, OtherBlob)]);

        stale.Conflicts.Should().Equal("a");
        result.Accepted.Should().Equal(new PushAccepted("a", 3));
        (await PullAllAsync(account)).Records.Should().ContainSingle().Which.Blob.Should().Equal(OtherBlob);
    }

    [Fact]
    public async Task Same_key_twice_in_a_batch_accepts_the_first_only()
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([new PushItem("a", 0, Blob), new PushItem("a", 0, OtherBlob)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1));
        result.Conflicts.Should().Equal("a");
        (await PullAllAsync(account)).Records.Should().ContainSingle().Which.Blob.Should().Equal(Blob);
    }

    [Fact]
    public async Task Second_item_of_a_key_based_on_the_first_is_accepted_and_wins()
    {
        var account = await NewAccountAsync();

        var result = await account.PushAsync([new PushItem("a", 0, Blob), new PushItem("a", 1, null), new PushItem("b", 0, Blob)]);

        result.Accepted.Should().Equal(new PushAccepted("a", 1), new PushAccepted("a", 2), new PushAccepted("b", 3));
        var records = (await PullAllAsync(account)).Records;
        records.Select(r => r.Seq).Should().Equal(2, 3);
        records[0].Blob.Should().BeNull();
    }
}
