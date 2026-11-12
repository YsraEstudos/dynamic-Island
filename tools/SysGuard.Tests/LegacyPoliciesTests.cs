namespace SysGuard.Tests;

using Microsoft.Win32;
using SysGuard;

public sealed class LegacyPoliciesTests
{
    [Fact]
    public void Entries_cover_hklm_and_hkcu_for_chrome_and_edge()
    {
        Assert.Equal(4, LegacyPolicies.Entries.Count);

        Assert.Contains(LegacyPolicies.Entries, entry =>
            entry.Hive == RegistryHive.LocalMachine
            && entry.KeyPath == @"SOFTWARE\Policies\Google\Chrome"
            && entry.ValueName == "ForceYouTubeRestrict");
        Assert.Contains(LegacyPolicies.Entries, entry =>
            entry.Hive == RegistryHive.LocalMachine
            && entry.KeyPath == @"SOFTWARE\Policies\Microsoft\Edge"
            && entry.ValueName == "ForceYouTubeRestrict");
        Assert.Contains(LegacyPolicies.Entries, entry =>
            entry.Hive == RegistryHive.CurrentUser
            && entry.KeyPath == @"SOFTWARE\Policies\Google\Chrome"
            && entry.ValueName == "ForceYouTubeRestrict");
        Assert.Contains(LegacyPolicies.Entries, entry =>
            entry.Hive == RegistryHive.CurrentUser
            && entry.KeyPath == @"SOFTWARE\Policies\Microsoft\Edge"
            && entry.ValueName == "ForceYouTubeRestrict");
    }

    [Fact]
    public void Remove_all_reports_only_the_values_that_existed()
    {
        // Simula um notebook onde a 1.0 gravou HKLM Chrome/Edge e o usuário tinha HKCU Chrome.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"HKLM\SOFTWARE\Policies\Google\Chrome\ForceYouTubeRestrict",
            @"HKLM\SOFTWARE\Policies\Microsoft\Edge\ForceYouTubeRestrict",
            @"HKCU\SOFTWARE\Policies\Google\Chrome\ForceYouTubeRestrict",
        };
        var visited = new List<string>();

        var removed = LegacyPolicies.RemoveAll(entry =>
        {
            visited.Add(entry.Description);
            return existing.Contains(entry.Description);
        });

        Assert.Equal(4, visited.Count);
        Assert.Equal(3, removed.Count);
        Assert.All(removed, entry => Assert.Contains(entry.Description, existing));
        Assert.DoesNotContain(removed, entry => entry.Hive == RegistryHive.CurrentUser
                                               && entry.KeyPath.Contains("Microsoft\\Edge", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_all_with_nothing_to_remove_returns_empty()
    {
        var visited = 0;
        var removed = LegacyPolicies.RemoveAll(_ =>
        {
            visited++;
            return false;
        });

        Assert.Empty(removed);
        Assert.Equal(LegacyPolicies.Entries.Count, visited);
    }

    [Fact]
    public void Remove_all_swallows_a_failing_remover_and_keeps_going()
    {
        int calls = 0;
        var removed = LegacyPolicies.RemoveAll(entry =>
        {
            calls++;
            if (entry.Hive == RegistryHive.LocalMachine) throw new UnauthorizedAccessException("sem admin");
            return true;
        });

        Assert.Equal(LegacyPolicies.Entries.Count, calls);
        // As duas entradas de HKCU foram removidas; as de HKLM lançaram e foram ignoradas.
        Assert.Equal(2, removed.Count);
        Assert.All(removed, entry => Assert.Equal(RegistryHive.CurrentUser, entry.Hive));
    }

    [Fact]
    public void Description_includes_the_hive_and_value_name()
    {
        var hklm = new LegacyPolicy(RegistryHive.LocalMachine,
            @"SOFTWARE\Policies\Google\Chrome", "ForceYouTubeRestrict");
        var hkcu = hklm with { Hive = RegistryHive.CurrentUser };

        Assert.Equal(@"HKLM\SOFTWARE\Policies\Google\Chrome\ForceYouTubeRestrict", hklm.Description);
        Assert.Equal(@"HKCU\SOFTWARE\Policies\Google\Chrome\ForceYouTubeRestrict", hkcu.Description);
    }

    [Fact]
    public void Legacy_value_name_is_the_youtube_restrict_policy()
    {
        Assert.Equal("ForceYouTubeRestrict", LegacyPolicies.YouTubeRestrictValue);
    }
}
