namespace SysGuard.Tests;

using SysGuard;

public sealed class HostsFileTests
{
    private const string Original = "127.0.0.1 localhost\r\n::1 localhost\r\n";

    [Fact]
    public void Compose_adds_marked_section_and_keeps_original()
    {
        var composed = HostsFile.Compose(Original, ["0.0.0.0 exemplo.com", ":: exemplo.com"]);
        Assert.True(HostsFile.HasSection(composed));
        Assert.Contains("127.0.0.1 localhost", composed);
        Assert.Contains("0.0.0.0 exemplo.com", composed);
        Assert.Contains(":: exemplo.com", composed);
        Assert.True(composed.IndexOf(HostsFile.BeginMarker, StringComparison.Ordinal) <
                    composed.IndexOf(HostsFile.EndMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void Strip_section_returns_original()
    {
        var composed = HostsFile.Compose(Original, ["0.0.0.0 exemplo.com"]);
        var stripped = HostsFile.StripSection(composed);
        Assert.Equal(Original.Replace("\r\n", "\n"), stripped);
        Assert.False(HostsFile.HasSection(stripped));
    }

    [Fact]
    public void Compose_is_idempotent()
    {
        var section = new[] { "0.0.0.0 exemplo.com" };
        var once = HostsFile.Compose(Original, section);
        var twice = HostsFile.Compose(once, section);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Strip_without_section_keeps_content()
    {
        Assert.Equal(Original.Replace("\r\n", "\n"), HostsFile.StripSection(Original));
    }

    [Fact]
    public void Apply_and_restore_on_disk_keeps_original_bytes()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var hosts = Path.Combine(directory, "hosts");
            var backup = Path.Combine(directory, "hosts.original");
            File.WriteAllText(hosts, Original);

            Assert.True(HostsFile.TryApply(hosts, backup, ["0.0.0.0 exemplo.com"], out var error), error);
            Assert.True(HostsFile.HasSection(File.ReadAllText(hosts)));
            Assert.Equal(Original, File.ReadAllText(backup));

            Assert.True(HostsFile.TryRestore(hosts, backup, out error), error);
            Assert.Equal(Original, File.ReadAllText(hosts));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void EnsureOriginalSnapshot_never_overwrites_an_existing_backup()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var hosts = Path.Combine(directory, "hosts");
            var backup = Path.Combine(directory, "hosts.original");
            File.WriteAllText(hosts, Original);

            HostsFile.EnsureOriginalSnapshot(hosts, backup);

            File.WriteAllText(hosts, "0.0.0.0 alterado.com\n");
            HostsFile.EnsureOriginalSnapshot(hosts, backup);

            Assert.Equal(Original, File.ReadAllText(backup));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Backup_keeps_raw_bytes_for_non_utf8_hosts()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var hosts = Path.Combine(directory, "hosts");
            var backup = Path.Combine(directory, "hosts.original");
            byte[] original = [0x31, 0x32, 0x37, 0x2E, 0x30, 0x2E, 0x30, 0x2E, 0x31, 0x20, 0x6C, 0x6F, 0x63, 0x61, 0x6C, 0x68, 0x6F, 0x73, 0x74, 0x0D, 0x0A, 0x23, 0x20, 0x61, 0xE7, 0xE3, 0x6F, 0x0D, 0x0A];
            File.WriteAllBytes(hosts, original);

            Assert.True(HostsFile.TryApply(hosts, backup, ["0.0.0.0 exemplo.com"], out var error), error);
            Assert.Equal(original, File.ReadAllBytes(backup));

            Assert.True(HostsFile.TryRestore(hosts, backup, out error), error);
            Assert.Equal(original, File.ReadAllBytes(hosts));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Apply_preserves_read_only_attribute()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var hosts = Path.Combine(directory, "hosts");
            var backup = Path.Combine(directory, "hosts.original");
            File.WriteAllText(hosts, Original);
            File.SetAttributes(hosts, FileAttributes.ReadOnly);

            Assert.True(HostsFile.TryApply(hosts, backup, ["0.0.0.0 exemplo.com"], out var error), error);
            Assert.True((File.GetAttributes(hosts) & FileAttributes.ReadOnly) != 0);

            File.SetAttributes(hosts, FileAttributes.Normal);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }
}

public sealed class BlockListTests
{
    [Theory]
    [InlineData("0.0.0.0 Exemplo.COM", "exemplo.com")]
    [InlineData("127.0.0.1 site.com.br", "site.com.br")]
    [InlineData("::1 outro.net", "outro.net")]
    [InlineData("dominio.com", "dominio.com")]
    [InlineData("  DOMINIO.com  ", "dominio.com")]
    public void Normalize_accepts_hosts_formats(string line, string expected)
    {
        Assert.Equal(expected, BlockList.Normalize(line));
    }

    [Theory]
    [InlineData("# comentário")]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("::1 localhost")]
    [InlineData("0.0.0.0 a b c")]
    [InlineData("site")]
    [InlineData("inválido!.com")]
    public void Normalize_rejects_invalid_lines(string line)
    {
        Assert.Null(BlockList.Normalize(line));
    }

    [Fact]
    public void Embedded_list_contains_well_known_domains()
    {
        Assert.True(BlockList.Domains.Count >= 1000, $"Lista pequena demais: {BlockList.Domains.Count}");
        Assert.Contains("pornhub.com", BlockList.Domains);
        Assert.Contains("xvideos.com", BlockList.Domains);
        Assert.Contains("xnxx.com", BlockList.Domains);
        Assert.All(BlockList.Domains, domain => Assert.DoesNotContain(" ", domain));
    }

    [Fact]
    public void Hosts_section_has_ipv4_and_ipv6_entries()
    {
        var section = BlockList.BuildHostsSection();
        Assert.Equal(BlockList.Domains.Count * 2, section.Count);
        Assert.Contains("0.0.0.0 " + BlockList.Domains[0], section);
        Assert.Contains(":: " + BlockList.Domains[0], section);
    }
}
