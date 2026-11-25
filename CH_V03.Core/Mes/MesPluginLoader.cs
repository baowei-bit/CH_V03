using System.Reflection;
using System.Runtime.Loader;

namespace CH_V03.Core.Mes;

public sealed class MesPluginLoader
{
    public IReadOnlyList<IMesPlugin> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plugins = new List<IMesPlugin>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            plugins.AddRange(LoadFromAssembly(file));
        }

        // 景旺插件是当前项目内置选项，构建后放在插件库中，配置页应可直接选择。
        var bundled = Path.Combine(directory, "..", ".plugin-library", "mes", "CH_V03.Mes.JingWang.dll");
        if (File.Exists(bundled)) plugins.AddRange(LoadFromAssembly(bundled));

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
        private readonly string _pluginLibraryDirectory;
        private readonly string _pluginDirectory;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            var activePluginDirectory = Path.GetDirectoryName(pluginPath) ?? string.Empty;
            _pluginDirectory = activePluginDirectory;
            var pluginsDirectory = Directory.GetParent(activePluginDirectory)?.FullName ?? string.Empty;
            _pluginLibraryDirectory = Path.Combine(pluginsDirectory, ".plugin-library", Path.GetFileName(activePluginDirectory));
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
            if (assemblyPath is not null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            var siblingAssembly = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(siblingAssembly)) return LoadFromAssemblyPath(siblingAssembly);

            var libraryAssemblyPath = Path.Combine(_pluginLibraryDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(libraryAssemblyPath) ? LoadFromAssemblyPath(libraryAssemblyPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath is not null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            var libraryNativePath = Path.Combine(_pluginLibraryDirectory, unmanagedDllName);
            return File.Exists(libraryNativePath) ? LoadUnmanagedDllFromPath(libraryNativePath) : IntPtr.Zero;
        }
    }
}
