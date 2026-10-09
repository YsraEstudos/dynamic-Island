using Island.Core.Audio;
using Island.Core.Fakes;

namespace Island.Core.Tests.Audio;

public sealed class FakeAudioMixerServiceTests
{
    [Fact]
    public void Demo_has_several_apps_and_at_least_one_inactive()
    {
        using var mixer = new FakeAudioMixerService();

        IReadOnlyList<AppAudioSession> sessions = mixer.Sessions;

        Assert.InRange(sessions.Count, 4, 5);
        Assert.Contains(sessions, s => s.Active);
        Assert.Contains(sessions, s => !s.Active);
    }

    [Fact]
    public void SetVolume_and_SetMuted_update_the_session_and_raise_Changed()
    {
        using var mixer = new FakeAudioMixerService();
        AppAudioSession first = mixer.Sessions[0];
        int raised = 0;
        mixer.Changed += (_, _) => raised++;

        mixer.SetVolume(first.Id, 150);
        mixer.SetMuted(first.Id, true);

        AppAudioSession updated = mixer.Sessions.Single(s => s.Id == first.Id);
        Assert.Equal(100, updated.Volume);
        Assert.True(updated.Muted);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Unknown_ids_are_ignored_without_raising_Changed()
    {
        using var mixer = new FakeAudioMixerService();
        int raised = 0;
        mixer.Changed += (_, _) => raised++;

        mixer.SetVolume("missing", 10);
        mixer.SetMuted("missing", true);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Peaks_stay_silent_until_metering_is_started()
    {
        using var mixer = new FakeAudioMixerService();

        Assert.All(mixer.Sessions, s => Assert.Equal(0.0, s.Peak));
    }

    [Fact]
    public void Metering_scope_moves_peaks_and_disposing_it_stops_the_timer()
    {
        using var mixer = new FakeAudioMixerService();
        var raised = new CountdownEvent(3);
        mixer.Changed += (_, _) => { if (raised.CurrentCount > 0) raised.Signal(); };

        IDisposable scope = mixer.BeginMetering();
        bool moved = raised.Wait(TimeSpan.FromSeconds(5));
        IReadOnlyList<AppAudioSession> whileMetering = mixer.Sessions;
        scope.Dispose();

        Assert.True(moved, "peaks should be updated while a metering scope is open");
        Assert.Contains(whileMetering, s => s.Active && PeakSmoother.IsAudible(s.Peak));
        Assert.All(mixer.Sessions, s => Assert.Equal(0.0, s.Peak));
    }
}
