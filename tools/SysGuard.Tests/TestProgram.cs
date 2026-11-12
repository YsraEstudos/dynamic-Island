using System.Reflection;

namespace SysGuard.Tests;

/// <summary>
/// Executor simples dos testes quando o testhost oficial do dotnet não pode rodar (por exemplo em um
/// sandbox de integridade baixa). Executa os mesmos [Fact]/[Theory][InlineData] do projeto:
///   dotnet run --project tools/SysGuard.Tests
/// O <c>dotnet test</c> continua funcionando normalmente fora do sandbox.
/// </summary>
internal static class TestProgram
{
    public static int Main()
    {
        var types = typeof(TestProgram).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        int passed = 0;
        int failed = 0;

        foreach (var type in types)
        {
            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .OrderBy(method => method.Name, StringComparer.Ordinal);

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<FactAttribute>() is null && method.GetCustomAttribute<TheoryAttribute>() is null)
                    continue;

                foreach (var arguments in Cases(method))
                {
                    string label = type.Name + "." + method.Name;
                    try
                    {
                        object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                        method.Invoke(instance, arguments);
                        passed++;
                        Console.WriteLine("PASS " + label);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Exception inner = ex is TargetInvocationException { InnerException: { } captured } ? captured : ex;
                        Console.WriteLine("FAIL " + label + " :: " + inner.GetType().Name + ": " + inner.Message);
                        if (inner is not Xunit.Sdk.XunitException && inner.StackTrace is { } stack)
                            Console.WriteLine("     " + stack.Split('\n').FirstOrDefault()?.Trim());
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed} passaram, {failed} falharam.");
        return failed == 0 ? 0 : 1;
    }

    private static IEnumerable<object?[]> Cases(MethodInfo method)
    {
        if (method.GetCustomAttribute<TheoryAttribute>() is null)
        {
            return [Array.Empty<object?>()];
        }

        var inlineData = method.GetCustomAttributes<InlineDataAttribute>().ToArray();
        if (inlineData.Length == 0)
        {
            throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name}: teoria sem InlineData não é suportada pelo executor local.");
        }

        var parameters = method.GetParameters();
        return inlineData.SelectMany(data => data.GetData(method)).Select(values =>
        {
            // InlineData(null) pode chegar como array nulo no GetData do xunit.
            if (values is null) return new object?[] { null };
            if (values.Length == parameters.Length) return values;
            if (parameters.Length == 1) return new object?[] { null };
            return values;
        });
    }
}
