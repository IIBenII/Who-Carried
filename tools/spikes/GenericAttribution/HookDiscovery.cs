using System.Reflection;

namespace GenericAttribution;

internal static class HookDiscovery
{
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static MethodInfo[] Find(Type root, IEnumerable<Type> types, bool metadataOnly = false) => types
        .Where(t => !t.IsAbstract && root.IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(InstanceMethods))
        .Where(m => !m.IsAbstract && m.IsVirtual && !m.ContainsGenericParameters
            && m.ReturnType.FullName == "System.Threading.Tasks.Task"
            && (metadataOnly ? HasRootSlot(root, m) : m.GetBaseDefinition().DeclaringType == root) && m.DeclaringType != root)
        // Harmony rejects an inherited MethodInfo reflected through the child type. Resolve
        // back to its actual declaration; this also deduplicates shared compatibility bridges.
        .Select(m => m.DeclaringType!.GetMethods(InstanceMethods | BindingFlags.DeclaredOnly)
            .Single(declared => declared.MetadataToken == m.MetadataToken))
        .Distinct().ToArray();

    // MetadataLoadContext does not implement GetBaseDefinition. Follow the same-signature
    // virtual declarations to the root, stopping at a new slot. Limited to this probe's
    // non-generic Task hooks; runtime installation uses GetBaseDefinition above.
    private static bool HasRootSlot(Type root, MethodInfo method)
    {
        Type[] parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
        for (Type? owner = method.DeclaringType; owner != null; owner = owner.BaseType)
        {
            MethodInfo? declaration = owner.GetMethods(InstanceMethods | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.IsVirtual && m.Name == method.Name
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
            if (declaration == null) continue;
            if (owner == root) return true;
            if ((declaration.Attributes & MethodAttributes.NewSlot) != 0) return false;
        }
        return false;
    }
}
