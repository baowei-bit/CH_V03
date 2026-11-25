using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.App.Services;
using CH_V03.App.Support;
using CH_V03.Core.Configuration;
using CH_V03.App.Localization;

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
        if (configuration.PreferJingWangLogin) UserName = "jingwang";
        LoginCommand = new RelayCommand(Login);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetField(ref _userName, value))
            {
                UiLanguage.SetForUser(value.Trim());
                Message = UiLanguage.Text("请输入账号和密码");
                OnPropertyChanged(nameof(Version));
            }
        }
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

    public string Version => $"{UiLanguage.Text("版本：")}{ApplicationVersion.Current}";

    public ICommand LoginCommand { get; }

    public ICommand CancelCommand { get; }

    private void Login()
    {
        if (_authenticationService.Login(_configuration, UserName.Trim(), Password, out var message))
        {
            _configuration.LoginJobNo = UserName.Trim();
            if (string.Equals(_configuration.LoginJobNo, "jingwang", StringComparison.OrdinalIgnoreCase))
                _configuration.PreferJingWangLogin = true;
            Close(true);
            return;
        }

        Message = UiLanguage.Text(message);
    }
}
