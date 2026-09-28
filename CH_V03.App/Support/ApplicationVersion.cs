using System.Reflection;

namespace CH_V03.App.Support;

/// <summary>
/// 软件对外显示的版本号。
/// 版本号由 CeHao.csproj 的 Version 属性统一提供，无需在此处重复维护。
/// </summary>
public static class ApplicationVersion
{
    public static string Current
    {
        get
        {
            var version = typeof(ApplicationVersion).Assembly.GetName().Version ?? new Version(3, 0, 0, 0);
            return $"CH_V{version.Major:00}.{version.Build:00}";
        }
    }
}
