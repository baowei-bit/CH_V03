using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace CH_V03.Core.Mes;

[SupportedOSPlatform("windows")]
public static class MesSecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret)) return string.Empty;
        if (!protectedSecret.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("MES 密码未加密，已拒绝使用。请在插件配置中重新输入密码。");
        var data = Convert.FromBase64String(protectedSecret[Prefix.Length..]);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser));
    }
}
