using System.Reflection;
using System.Runtime.Loader;

namespace CH_V03.Core.ResultExport;

public sealed class ResultExportPluginLoader
{
    public IReadOnlyList<IResultExportPlugin> LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return Array.Empty<IResultExportPlugin>();
        }

        var plugins = new List<IResultExportPlugin>();
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

    public IReadOnlyList<IResultExportPlugin> LoadFromAssembly(string assemblyPath)
    {
        try
        {
            var context = new ExportPluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            return assembly.GetTypes()
                .Where(type => typeof(IResultExportPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
                .Select(type => (IResultExportPlugin)Activator.CreateInstance(type)!)
                .ToList();
        }
        catch
        {
            return Array.Empty<IResultExportPlugin>();
        }
    }

    private sealed class ExportPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginLibraryDirectory;

        public ExportPluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            var activePluginDirectory = Path.GetDirectoryName(pluginPath) ?? string.Empty;
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
