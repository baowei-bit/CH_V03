using System.IO;

namespace CH_V03.App.Services;

internal static class AppPaths
{
    public static string AirtightnessDirectory => Path.Combine(AppContext.BaseDirectory, "data", "airtightness");
    public static string LoginSelectionFile => Path.Combine(AppContext.BaseDirectory, "config", "last-module.json");

    public static string ConfigurationFile => Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "appsettings.json");

    public static string ProjectProfilesDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "profiles");
}
