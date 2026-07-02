using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.App.Services;
using BW_V01.Core.Configuration;

namespace BW_V01.App.ViewModels;

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
