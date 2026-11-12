namespace SysGuard.Tests;

using SysGuard;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Create_and_verify_round_trip()
    {
        var record = PasswordHasher.Create("senha-forte-123");
        Assert.True(PasswordHasher.Verify("senha-forte-123", record));
        Assert.False(PasswordHasher.Verify("senha-forte-124", record));
        Assert.False(PasswordHasher.Verify(string.Empty, record));
    }

    [Fact]
    public void Same_password_gets_different_salt()
    {
        var first = PasswordHasher.Create("mesma-senha");
        var second = PasswordHasher.Create("mesma-senha");
        Assert.NotEqual(first.SaltBase64, second.SaltBase64);
        Assert.NotEqual(first.HashBase64, second.HashBase64);
        Assert.True(PasswordHasher.Verify("mesma-senha", first));
        Assert.True(PasswordHasher.Verify("mesma-senha", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    public void Weak_passwords_are_rejected(string? password)
    {
        Assert.False(PasswordHasher.IsAcceptable(password, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Null_password_is_rejected()
    {
        Assert.False(PasswordHasher.IsAcceptable(null, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("senha com espacos")]
    [InlineData("A!b@C#d$")]
    public void Valid_passwords_are_accepted(string password)
    {
        Assert.True(PasswordHasher.IsAcceptable(password, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Newline_in_password_is_rejected()
    {
        Assert.False(PasswordHasher.IsAcceptable("senha\nquebrada", out _));
    }

    [Fact]
    public void Corrupted_record_never_verifies()
    {
        var corrupted = new PasswordRecord(PasswordHasher.DefaultIterations, "não-é-base64", "também-não");
        Assert.False(PasswordHasher.Verify("qualquer", corrupted));
    }
}

public sealed class PasswordConfigTests
{
    [Fact]
    public void Save_and_load_preserves_hash()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var path = Path.Combine(directory, "config.dat");
            PasswordConfig.Create(path, "minha-senha-1");
            Assert.True(PasswordConfig.Exists(path));
            Assert.True(PasswordConfig.Verify(path, "minha-senha-1"));
            Assert.False(PasswordConfig.Verify(path, "minha-senha-2"));

            var loaded = PasswordConfig.Load(path);
            Assert.Equal(PasswordHasher.DefaultIterations, loaded.Iterations);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Corrupted_file_is_rejected_with_invalid_data()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var path = Path.Combine(directory, "config.dat");
            File.WriteAllText(path, "lixo\n1\n2\n3\n");
            Assert.Throws<InvalidDataException>(() => PasswordConfig.Load(path));
            Assert.False(PasswordConfig.Verify(path, "x"));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Missing_file_never_verifies()
    {
        var path = Path.Combine(TestPaths.NewDirectory(), "ausente.dat");
        Assert.False(PasswordConfig.Verify(path, "x"));
    }
}
