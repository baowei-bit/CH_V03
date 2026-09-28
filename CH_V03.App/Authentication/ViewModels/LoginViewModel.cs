using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.App.Services;
using CH_V03.App.Support;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class LoginViewModel : DialogViewModelBase
{
    private readonly AuthenticationService _authenticationService;
    private readonly UserConfiguration _configuration;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _message = "请输入账号和密码";

    public LoginViewModel(AuthenticationService authenticationService, UserConfiguration configuration)
    {
        _authenticationService = authenticationService;
        _configuration = configuration;
        UserName = configuration.LoginJobNo;
        LoginCommand = new RelayCommand(Login);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    public string Version => $"版本：{ApplicationVersion.Current}";

    public ICommand LoginCommand { get; }

    public ICommand CancelCommand { get; }

    private void Login()
    {
        if (_authenticationService.Login(_configuration, UserName.Trim(), Password, out var message))
        {
            _configuration.LoginJobNo = UserName.Trim();
            Close(true);
            return;
        }

        Message = message;
    }
}
