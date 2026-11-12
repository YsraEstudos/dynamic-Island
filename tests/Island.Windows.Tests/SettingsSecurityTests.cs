using Island.App.ViewModels;
using Island.Core.Configuration;

namespace Island.Windows.Tests;

/// <summary>
/// PC-02: the phone block is a protection, not a look-and-feel preference. During an Angry session its fields are
/// locked, disabling it outside a session needs a typed confirmation, and "Reset to defaults" never weakens it.
/// </summary>
public sealed class SettingsSecurityTests
{
    private bool _locked;
    private int _confirmations;
    private bool _confirmAnswer = true;
    private IslandSettings _applied = new();

    private SettingsViewModel Create(IslandSettings initial)
    {
        _applied = initial;
        return new SettingsViewModel(initial, ["Primary"], s => _applied = s,
            getCurrent: () => _applied,
            securityLocked: () => _locked,
            confirmSecurityChange: () =>
            {
                _confirmations++;
                return _confirmAnswer;
            });
    }

    private static IslandSettings Protected() => new() { PhoneBlockEnabled = true, PhoneFcmToken = "token" };

    [Fact]
    public void The_phone_field_cannot_be_turned_off_during_an_angry_session()
    {
        var vm = Create(Protected());
        _locked = true;

        vm.PhoneBlockEnabled = false;

        Assert.True(vm.PhoneBlockEnabled);
        Assert.True(_applied.PhoneBlockEnabled);
        Assert.Equal(0, _confirmations); // the lock is stronger than the phrase prompt
        Assert.False(vm.SecuritySettingsEditable);
    }

    [Fact]
    public void The_phone_token_cannot_be_changed_during_an_angry_session()
    {
        var vm = Create(Protected());
        _locked = true;

        vm.PhoneFcmToken = "outro-token";

        Assert.Equal("token", vm.PhoneFcmToken);
        Assert.Equal("token", _applied.PhoneFcmToken);
    }

    [Fact]
    public void Outside_a_session_disabling_the_phone_block_needs_the_typed_confirmation()
    {
        var vm = Create(Protected());
        _confirmAnswer = false;

        vm.PhoneBlockEnabled = false;

        Assert.True(vm.PhoneBlockEnabled);
        Assert.Equal(1, _confirmations);
        Assert.True(_applied.PhoneBlockEnabled);

        _confirmAnswer = true;
        vm.PhoneBlockEnabled = false;

        Assert.False(vm.PhoneBlockEnabled);
        Assert.False(_applied.PhoneBlockEnabled);
        Assert.Equal(2, _confirmations);
    }

    [Fact]
    public void Turning_the_phone_block_on_does_not_ask_for_confirmation()
    {
        var vm = Create(new IslandSettings { PhoneBlockEnabled = false, PhoneFcmToken = "token" });

        vm.PhoneBlockEnabled = true;

        Assert.True(vm.PhoneBlockEnabled);
        Assert.Equal(0, _confirmations);
    }

    [Fact]
    public void Reset_to_defaults_keeps_the_phone_link_and_its_enabled_flag()
    {
        var vm = Create(Protected());

        vm.ResetToDefaultsCommand.Execute(null);

        Assert.True(_applied.PhoneBlockEnabled);
        Assert.Equal("token", _applied.PhoneFcmToken);
    }

    [Fact]
    public void The_settings_window_is_told_when_the_session_starts_and_ends()
    {
        var vm = Create(Protected());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _locked = true;
        vm.NotifySecurityStateChanged();

        Assert.Contains(nameof(SettingsViewModel.SecuritySettingsEditable), changed);
        Assert.Contains(nameof(SettingsViewModel.PhoneBlockEnabled), changed);
        Assert.False(vm.SecuritySettingsEditable);
    }
}
