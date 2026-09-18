using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GenericAttribution;

internal static class InspectAssemblies
{
    // Metadata only: never executes a game/mod constructor, initializer, or hook.
    public static int Run(string gameData, string[] modPaths)
    {
        string[] paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Concat(Directory.GetFiles(gameData, "*.dll"))
            .Concat(modPaths).Where(IsManaged)
            .DistinctBy(path => AssemblyName.GetAssemblyName(path).Name, StringComparer.OrdinalIgnoreCase).ToArray();
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
        string gamePath = Path.Combine(gameData, "sts2.dll");
        var game = context.LoadFromAssemblyPath(gamePath);
        var model = game.GetType("MegaCrit.Sts2.Core.Models.AbstractModel", throwOnError: true)!;
        var allTargets = new HashSet<MethodInfo>();
        foreach (string path in new[] { gamePath }.Concat(modPaths))
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
            var types = assembly.GetTypes().Where(t => !t.IsAbstract && model.IsAssignableFrom(t)).ToArray();
            var targets = HookDiscovery.Find(model, types, metadataOnly: true);
            allTargets.UnionWith(targets);
            Console.WriteLine($"ASSEMBLY {assembly.GetName().Name}: {types.Length} concrete models; {targets.Length} distinct implemented Task hook methods");
            Console.WriteLine($"SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
            // List shared inherited methods as well as each distinct declaration: bridge methods matter.
            foreach (var grouping in targets.GroupBy(m => m.DeclaringType!.FullName).OrderBy(g => g.Key))
                Console.WriteLine($"  {grouping.Key}: {string.Join(", ", grouping.Select(m => m.Name).Distinct().Order())}");
        }
        Console.WriteLine($"TOTAL {allTargets.Count} distinct candidate hook methods (metadata discovery only, not patched)");
        foreach (var hook in allTargets.GroupBy(m => m.Name).OrderBy(g => g.Key))
            Console.WriteLine($"HOOK {hook.Key}: {hook.Count()} declarations");
        return 0;
    }

    private static bool IsManaged(string path)
    {
        try { _ = AssemblyName.GetAssemblyName(path); return true; }
        catch (BadImageFormatException) { return false; }
    }
}
