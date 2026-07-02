using System.IO;

namespace BW_V01.App.Services;

internal static class AppPaths
{
    public static string ConfigurationFile => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "appsettings.json");

    public static string ProjectProfilesDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "profiles");
}
