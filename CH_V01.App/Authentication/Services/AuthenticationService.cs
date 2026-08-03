using CH_V01.Core.Configuration;

namespace CH_V01.App.Services;

public sealed class AuthenticationService
{
    public UserSession CurrentSession { get; private set; } = UserSession.Guest;

    public bool Login(UserConfiguration configuration, string userName, string password, out string message)
    {
        var account = configuration.Accounts.FirstOrDefault(item =>
            string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase));

        if (account is null)
        {
            message = "账号不存在";
            return false;
        }

        if (!string.Equals(account.Password, password, StringComparison.Ordinal))
        {
            message = "密码不正确";
            return false;
        }

        CurrentSession = new UserSession
        {
            UserName = account.UserName,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.UserName : account.DisplayName,
            Role = account.Role,
            IsAuthenticated = true
        };
        message = "登录成功";
        return true;
    }

    public bool LoginAsDefaultOperator(UserConfiguration configuration, out string message)
    {
        var account = configuration.Accounts.FirstOrDefault(item =>
            string.Equals(item.UserName, "operator", StringComparison.OrdinalIgnoreCase)
            && item.Role == UserRole.Operator);

        if (account is null)
        {
            message = "未找到操作员账号，请关闭默认操作员登录后使用账号密码登录。";
            return false;
        }

        CurrentSession = new UserSession
        {
            UserName = account.UserName,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.UserName : account.DisplayName,
            Role = UserRole.Operator,
            IsAuthenticated = true
        };
        message = "已自动以操作员身份登录";
        return true;
    }

    public void Logout()
    {
        CurrentSession = UserSession.Guest;
    }
}

public sealed class UserSession
{
    public static UserSession Guest { get; } = new()
    {
        UserName = "guest",
        DisplayName = "未登录",
        Role = UserRole.Operator,
        IsAuthenticated = false
    };

    public string UserName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public UserRole Role { get; init; } = UserRole.Operator;

    public bool IsAuthenticated { get; init; }

    public bool CanManageSystem => Role is UserRole.Engineer or UserRole.Administrator;

    public bool CanManageTestItems => Role is UserRole.Administrator;

    public bool CanManageUsers => Role is UserRole.Engineer or UserRole.Administrator;

    public bool CanImportProjectProfile => Role is UserRole.Administrator;

    public bool CanViewAnalysis => Role is UserRole.Technician or UserRole.Engineer or UserRole.Administrator;

    public string RoleText => Role switch
    {
        UserRole.Operator => "操作员",
        UserRole.Technician => "技术员",
        UserRole.Engineer => "工程师",
        UserRole.Administrator => "管理员",
        _ => Role.ToString()
    };
}
