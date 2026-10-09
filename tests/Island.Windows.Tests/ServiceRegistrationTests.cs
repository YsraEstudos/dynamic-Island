using Island.App.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Island.Windows.Tests;

/// <summary>
/// Guards against the app failing to start because a constructor needs something nobody registered
/// (v0.4.12 shipped with QuickNotesWindowManager needing an unregistered Func&lt;bool&gt;).
/// </summary>
public sealed class ServiceRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryRegisteredService_CanBeResolved(bool demo)
    {
        var services = new ServiceCollection();
        ServiceRegistration.Configure(services, demo, NullLoggerFactory.Instance);

        // ValidateOnBuild checks every constructor dependency without creating the WPF objects.
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider);
    }
}
