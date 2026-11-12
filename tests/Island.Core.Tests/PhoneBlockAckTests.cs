using Island.Core.Phone;

namespace Island.Core.Tests;

public sealed class PhoneBlockAckTests
{
    // Vector computed independently: sha256("dynamic-island-ack-v1:" + token), first 32 lowercase hex chars.
    private const string Token = "token-de-teste";

    [Fact]
    public void The_topic_is_derived_from_the_fcm_token_exactly_like_the_android_app()
    {
        Assert.Equal("di-ack-1fac79b3b4ef8b220eb2588757d9fe2e", PhoneBlockAck.TopicFor(Token));
    }

    [Fact]
    public void The_topic_is_stable_trims_the_token_and_ignores_case_of_the_prefix()
    {
        Assert.Equal(PhoneBlockAck.TopicFor(Token), PhoneBlockAck.TopicFor("  " + Token + "  "));
        Assert.NotEqual(PhoneBlockAck.TopicFor(Token), PhoneBlockAck.TopicFor(Token + "2"));
        Assert.Equal("di-ack-", PhoneBlockAck.TopicFor(Token)[..7]);
        Assert.Equal(39, PhoneBlockAck.TopicFor(Token).Length);
    }

    [Fact]
    public void A_complete_ack_payload_is_parsed()
    {
        const string payload = """
            {"v":1,"id":"abc123","applied":true,"a11y":true,"admin":true,"level":"full","endMs":1762882692123,"ts":1762881282123,"gapMs":0}
            """;

        Assert.True(PhoneBlockAck.TryParse(payload, out PhoneBlockAck? ack));
        Assert.NotNull(ack);
        Assert.Equal("abc123", ack.Id);
        Assert.True(ack.Applied);
        Assert.True(ack.Accessibility);
        Assert.True(ack.Admin);
        Assert.Equal("full", ack.Level);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762882692123), ack.End);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762881282123), ack.Timestamp);
        Assert.True(ack.IsConfirmed);
        Assert.Null(ack.FailureReason());
    }

    [Theory]
    [InlineData("""{"id":"abc","applied":true}""", false)] // missing a11y must never confirm
    [InlineData("""{"id":"abc","applied":true,"a11y":false}""", false)]
    [InlineData("""{"id":"abc","applied":false,"a11y":true}""", false)]
    [InlineData("""{"id":"abc","applied":true,"a11y":true}""", true)]
    public void An_ack_confirms_only_with_an_id_applied_and_a11y(string payload, bool expected)
    {
        Assert.True(PhoneBlockAck.TryParse(payload, out PhoneBlockAck? ack));
        Assert.NotNull(ack);
        Assert.Equal("abc", ack.Id);
        Assert.Equal(expected, ack.IsConfirmed);
    }

    [Fact]
    public void Booleans_and_epochs_are_read_tolerantly()
    {
        const string payload = """
            {"id":"abc","applied":1,"a11y":"true","admin":0,"endMs":"1762882692123"}
            """;

        Assert.True(PhoneBlockAck.TryParse(payload, out PhoneBlockAck? ack));
        Assert.NotNull(ack);
        Assert.True(ack.Applied);
        Assert.True(ack.Accessibility);
        Assert.False(ack.Admin);
        Assert.NotNull(ack.End);
    }

    [Theory]
    [InlineData("""{"event":"open","message":null}""")]
    [InlineData("""{"event":"keepalive"}""")]
    [InlineData("""{"event":"poll_request"}""")]
    [InlineData("""{"event":"message","message":"não é json"}""")]
    [InlineData("""{"event":"message"}""")]
    [InlineData("")]
    public void Non_ack_ntfy_lines_are_ignored(string line)
    {
        Assert.False(PhoneBlockAck.TryParseNtfyLine(line, out _));
    }

    [Fact]
    public void An_ntfy_message_line_is_parsed_into_its_ack()
    {
        const string line = """
            {"id":"ntfy-id","time":1762881282,"event":"message","topic":"di-ack-x","message":"{\"v\":1,\"id\":\"abc\",\"applied\":true,\"a11y\":true,\"endMs\":1762882692123}"}
            """;

        Assert.True(PhoneBlockAck.TryParseNtfyLine(line, out PhoneBlockAck? ack));
        Assert.NotNull(ack);
        Assert.Equal("abc", ack.Id);
        Assert.True(ack.IsConfirmed);
    }

    [Theory]
    [InlineData("guardian", true)]
    [InlineData("guardian-revive", true)]
    [InlineData("GUARDIAN", true)]
    [InlineData("app", false)]
    [InlineData(null, false)]
    public void Guardian_reasons_are_recognized(string? reason, bool expected)
    {
        var ack = new PhoneBlockAck("", true, true, false, null, null, null, reason);
        Assert.Equal(expected, ack.IsGuardianStatus);
    }

    [Fact]
    public void A_guardian_heartbeat_without_an_id_is_parsed()
    {
        // The Android guardian watches accessibility from inside the app and never sees the pending FCM message id.
        const string payload = """{"v":1,"reason":"guardian-revive","applied":true,"a11y":true,"endMs":1762882692123}""";

        Assert.True(PhoneBlockAck.TryParse(payload, out PhoneBlockAck? ack));
        Assert.NotNull(ack);
        Assert.Equal(string.Empty, ack.Id);
        Assert.True(ack.IsGuardianStatus);
        Assert.True(ack.IsConfirmed);
        Assert.NotNull(ack.End);
    }

    [Theory]
    [InlineData("""{"applied":true,"a11y":true}""")]
    [InlineData("""{"id":"","applied":true,"a11y":true}""")]
    [InlineData("""{"reason":"app","applied":true,"a11y":true}""")]
    [InlineData("""{"id":"abc"}""")]
    [InlineData("not json")]
    [InlineData("""[1,2,3]""")]
    [InlineData("")]
    public void An_id_less_ack_without_a_guardian_reason_is_refused(string payload)
    {
        Assert.False(PhoneBlockAck.TryParse(payload, out _));
    }

    [Fact]
    public void The_failure_reasons_are_user_facing()
    {
        Assert.Null(PhoneBlockAck.FailureReason(applied: true, accessibility: true));
        Assert.Contains("acessibilidade", PhoneBlockAck.FailureReason(applied: true, accessibility: false));
        Assert.Contains("não aplicou", PhoneBlockAck.FailureReason(applied: false, accessibility: true));
        Assert.Contains("acessibilidade", PhoneBlockAck.FailureReason(applied: false, accessibility: false));
    }
}

public sealed class PhoneBlockTrackerTests
{
    private DateTimeOffset _now = new(2026, 11, 11, 14, 0, 0, TimeSpan.FromHours(-3));

    private PhoneBlockTracker Tracker() => new(() => _now);

    private static PhoneBlockAck Ack(string id, bool applied = true, bool a11y = true, DateTimeOffset? end = null) =>
        new(id, applied, a11y, Admin: true, Level: "full", End: end, Timestamp: null);

    private static DateTimeOffset End(DateTimeOffset from) => from.AddMinutes(25);

    [Fact]
    public void A_new_block_awaits_the_ack_and_only_its_own_id_confirms_it()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));

        Assert.Equal(PhoneBlockStatus.Awaiting, tracker.Status);
        Assert.False(tracker.Observe(Ack("other-id"), out bool changed));
        Assert.False(changed);
        Assert.Equal(PhoneBlockStatus.Awaiting, tracker.Status);

        Assert.True(tracker.Observe(Ack("block-1"), out changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);
        Assert.Null(tracker.FailureReason);
    }

    [Fact]
    public void An_ack_without_accessibility_fails_the_block_with_a_reason()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));

        Assert.True(tracker.Observe(Ack("block-1", a11y: false), out bool changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);
        Assert.Contains("acessibilidade", tracker.FailureReason!);
    }

    [Fact]
    public void The_timeout_fails_an_awaiting_block_only_after_twenty_seconds()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));

        _now += TimeSpan.FromSeconds(19);
        Assert.False(tracker.CheckTimeout());
        Assert.Equal(PhoneBlockStatus.Awaiting, tracker.Status);

        _now += TimeSpan.FromSeconds(2);
        Assert.True(tracker.CheckTimeout());
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);
        Assert.Contains("sem confirmação", tracker.FailureReason!);
        Assert.False(tracker.CheckTimeout()); // reports the change once
    }

    [Fact]
    public void A_late_ack_after_the_timeout_upgrades_the_block()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));
        _now += TimeSpan.FromSeconds(21);
        Assert.True(tracker.CheckTimeout());

        Assert.True(tracker.Observe(Ack("block-1"), out bool changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);
    }

    [Fact]
    public void A_confirmed_heartbeat_keeps_the_seal_and_a_failing_one_revokes_it()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));
        tracker.Observe(Ack("block-1"), out _);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);

        _now += TimeSpan.FromMinutes(1);
        Assert.True(tracker.Observe(Ack("block-1"), out bool changed));
        Assert.False(changed);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);

        Assert.True(tracker.Observe(Ack("block-1", applied: false), out changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);
    }

    [Fact]
    public void The_later_end_from_an_ack_wins_and_the_seal_has_a_grace_window()
    {
        var tracker = Tracker();
        DateTimeOffset end = End(_now);
        tracker.Expect("block-1", end);
        tracker.Observe(Ack("block-1", end: end.AddMinutes(5)), out _);

        Assert.Equal(end.AddMinutes(5), tracker.End);
        Assert.True(tracker.IsSealLive(TimeSpan.FromMinutes(1)));

        _now = end.AddMinutes(6).AddSeconds(30);
        Assert.False(tracker.IsSealLive(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Reset_forgets_the_block()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));
        tracker.Observe(Ack("block-1"), out _);

        tracker.Reset();

        Assert.Equal(PhoneBlockStatus.Idle, tracker.Status);
        Assert.Null(tracker.MessageId);
        Assert.False(tracker.Observe(Ack("block-1"), out _));
    }

    private static PhoneBlockAck GuardianAck(bool applied = true, bool a11y = true, string reason = "guardian-revive") =>
        new(Id: string.Empty, applied, a11y, Admin: true, Level: "full", End: null, Timestamp: null, Reason: reason);

    [Fact]
    public void A_guardian_heartbeat_confirms_the_expected_block_without_the_message_id()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));

        Assert.True(tracker.Observe(GuardianAck(), out bool changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);
        Assert.Null(tracker.FailureReason);
    }

    [Fact]
    public void A_guardian_heartbeat_revives_a_block_that_had_timed_out()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));
        _now += TimeSpan.FromSeconds(21);
        Assert.True(tracker.CheckTimeout());
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);

        Assert.True(tracker.Observe(GuardianAck(reason: "guardian"), out bool changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Confirmed, tracker.Status);
    }

    [Fact]
    public void A_guardian_heartbeat_that_reports_no_accessibility_fails_the_block()
    {
        var tracker = Tracker();
        tracker.Expect("block-1", End(_now));

        Assert.True(tracker.Observe(GuardianAck(a11y: false), out bool changed));
        Assert.True(changed);
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);
        Assert.Contains("acessibilidade", tracker.FailureReason!);
        Assert.True(tracker.Observe(GuardianAck(applied: false, reason: "guardian"), out _));
        Assert.Equal(PhoneBlockStatus.Failed, tracker.Status);
    }

    [Fact]
    public void A_guardian_heartbeat_is_ignored_when_no_block_is_expected()
    {
        var tracker = Tracker();

        Assert.False(tracker.Observe(GuardianAck(), out _));
        Assert.Equal(PhoneBlockStatus.Idle, tracker.Status);
    }
}
