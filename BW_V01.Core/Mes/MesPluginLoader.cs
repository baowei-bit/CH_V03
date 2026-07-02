using System.Reflection;
using System.Runtime.Loader;

namespace BW_V01.Core.Mes;

public sealed class MesPluginLoader
{
    public IReadOnlyList<IMesPlugin> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return Array.Empty<IMesPlugin>();
        }

        var plugins = new List<IMesPlugin>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            plugins.AddRange(LoadFromAssembly(file));
        }

        return plugins
            .GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(plugin => plugin.Name)
            .ToList();
    }

    public IReadOnlyList<IMesPlugin> LoadFromAssembly(string assemblyPath)
    {
        try
        {
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            return assembly.GetTypes()
                .Where(type => typeof(IMesPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
                .Select(type => (IMesPlugin)Activator.CreateInstance(type)!)
                .ToList();
        }
        catch
        {
            return Array.Empty<IMesPlugin>();
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            if (sharedAssembly is not null)
            {
                return sharedAssembly;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }
    }
}
