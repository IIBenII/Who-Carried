using System.Reflection;

namespace RunRecap.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
}

public sealed class CheckFailed(string message) : Exception(message);

public static class Check
{
    public static void Equal<T>(T expected, T actual, string what = "value")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new CheckFailed($"{what}: expected <{expected}> but got <{actual}>");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new CheckFailed($"expected true: {what}");
    }

    public static void Near(double expected, double actual, string what = "value", double tolerance = 1e-9)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new CheckFailed($"{what}: expected <{expected}> but got <{actual}>");
    }
}

public static class TestRunner
{
    /// <summary>Runs every public static [Test] method; returns 0 if all pass (and at least one ran).</summary>
    public static int RunAll(Assembly assembly, string? filter)
    {
        List<MethodInfo> tests = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();
        int failed = 0;
        foreach (MethodInfo test in tests)
        {
            string name = $"{test.DeclaringType!.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"PASS {name}");
            }
            catch (TargetInvocationException e)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {e.InnerException?.Message}");
            }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 && tests.Count > 0 ? 0 : 1;
    }
}
