using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.App.Services;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly SemaphoreSlim _jwShiftGate = new(1, 1);
    private (string Employee, string Equipment)? _jwShiftIdentity;
    private bool _jwProcessing;
    private bool _jwScanning;

    public bool IsJingWangAccount => _authenticationService.CurrentSession.IsAuthenticated &&
        string.Equals(_authenticationService.CurrentSession.UserName, "jingwang", StringComparison.OrdinalIgnoreCase);
    public bool ShowStandardStatistics => !IsJingWangAccount;
    public bool CanEditJingWangFields => !_jwScanning && !_jwProcessing && TestState != "TEST" && (TestState != "READY" || string.IsNullOrEmpty(CurrentBarcode));
    public string JwEmployee { get => JwOption("EmployeeCardCode"); set => SetJwOption("EmployeeCardCode", value); }
    public string JwWorkOrder { get => JwOption("WorkOrder"); set => SetJwOption("WorkOrder", value); }
    public bool KeyboardSnMode => IsJingWangAccount && _configuration.User.ManualScanMode && _configuration.User.JingWangKeyboardSn;
    public async Task SubmitKeyboardSnAsync(string text)
    {
        if (!KeyboardSnMode || !CanEditJingWangFields || IsPopupVisible) return;
        if (string.IsNullOrWhiteSpace(text))
            await ShowRuntimePopupAsync("请输入 SN 后按回车", true, true);
        else
            await HandleScannerTextAsync(text, keyboard: true);
    }

    public string JwLot { get => JwOption("Container"); set => SetJwOption("Container", value); }
    public string JwProduct { get => JwOption("Product"); set => SetJwOption("Product", value); }
    public string JwEquipment { get => JwOption("EquipmentCode"); set => SetJwOption("EquipmentCode", value); }
    public string JwSpec { get => JwOption("SpecCode"); set => SetJwOption("SpecCode", value); }
    public string JwSummaryPath
    {
        get => _configuration.Output.CsvDirectory;
        set { if (CanEditJingWangFields) { _configuration.Output.CsvDirectory = value; _configurationStore.Save(_configuration); } }
    }
    public string JwPiecePath
    {
        get => _configuration.Output.SinglePieceCsvDirectory;
        set { if (CanEditJingWangFields) { _configuration.Output.SinglePieceCsvDirectory = value; _configurationStore.Save(_configuration); } }
    }

    public ICommand BrowseJwPathCommand => new RelayCommand(parameter =>
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择 CSV 保存目录" };
        if (CanEditJingWangFields && picker.ShowDialog() == true)
        {
            if (Equals(parameter, "summary")) JwSummaryPath = picker.FolderName;
            else JwPiecePath = picker.FolderName;
            OnPropertyChanged(Equals(parameter, "summary") ? nameof(JwSummaryPath) : nameof(JwPiecePath));
        }
    });

    private bool UseJingWangRecords => MesRuntimeService.IsJingWang(_configuration) && (!IsJingWangAccount || _configuration.Mes.OnlineMode);

    private string JwOption(string key) => _configuration.Mes.PluginOptions.GetValueOrDefault(key, "");
    private void SetJwOption(string key, string value)
    {
        if (!CanEditJingWangFields || JwOption(key) == value.Trim()) return;
        _configuration.Mes.PluginOptions[key] = value.Trim();
        if (key == "EmployeeCardCode") _configuration.Mes.UserNo = value.Trim();
        _configurationStore.Save(_configuration);
        OnPropertyChanged(nameof(HeaderTitle));
    }

    private void RaiseJingWangFields()
    {
        foreach (var name in new[] { nameof(IsJingWangAccount), nameof(ShowStandardStatistics), nameof(CanEditJingWangFields),
                 nameof(KeyboardSnMode), nameof(JwLot), nameof(JwEmployee), nameof(JwWorkOrder), nameof(JwProduct), nameof(JwEquipment), nameof(JwSpec), nameof(JwSummaryPath), nameof(JwPiecePath) })
            OnPropertyChanged(name);
    }

    private static void SetJingWangMesSwitch(AppConfiguration configuration, bool online)
    {
        var value = online ? "true" : "false";
        configuration.Mes.OnlineMode = online;
        configuration.Mes.PluginOptions["MainMesOnline"] = value;
        foreach (var key in new[] { "EnableEmployee", "EnableInspection", "EnableInspectionResult", "EnableWebApi" })
            configuration.Mes.PluginOptions[key] = value;
    }

    private void ConfigureJingWangSession()
    {
        _configuration.Mes.SelectedPlugin = "JingWang.Eap";
        _configuration.Mes.Enabled = true;
        SetJingWangMesSwitch(_configuration, !string.Equals(JwOption("MainMesOnline"), "false", StringComparison.OrdinalIgnoreCase));
        _configuration.Popup.Enabled = true;
        _configuration.Popup.RequireConfirmation = true;
        _configuration.Output.SinglePieceCsvEnabled = true;
        if (!_configuration.Export.EnabledPluginIds.Contains("Default.Csv", StringComparer.OrdinalIgnoreCase))
            _configuration.Export.EnabledPluginIds.Add("Default.Csv");
        _configuration.Mes.UserNo = JwEmployee;
        _configurationStore.Save(_configuration);
    }

    private bool AcceptJingWangSettings(AppConfiguration edited)
    {
        if (!IsJingWangAccount) return true;
        if (!CanEditJingWangFields || !MesRuntimeService.IsJingWang(edited) || !edited.Mes.Enabled ||
            edited.Mes.OnlineMode != _configuration.Mes.OnlineMode)
        {
            _configurationDialogService.ShowAlert("景旺设置", "请在测试结束后修改配置。MES 在线或离线请使用主界面的 MES 开关。");
            return false;
        }
        edited.Mes.UserNo = edited.Mes.PluginOptions.GetValueOrDefault("EmployeeCardCode", "");
        return true;
    }

    private async Task<MesResult> EnsureJingWangShiftAsync()
    {
        await _jwShiftGate.WaitAsync();
        try
        {
            var identity = (JwEmployee, JwEquipment);
            if (string.IsNullOrWhiteSpace(JwEmployee) || string.IsNullOrWhiteSpace(JwEquipment)) return MesResult.Fail("请填写人员工号和设备编码");
            if (_jwShiftIdentity == identity) return MesResult.Success("景旺员工已上机");
            if (_jwShiftIdentity.HasValue)
            {
                var logout = await _mesRuntimeService.EndJingWangShiftAsync(_configuration);
                if (!logout.IsSuccess) return logout;
                _jwShiftIdentity = null;
            }
            var result = await _mesRuntimeService.TestConnectionAsync(_configuration);
            if (!result.IsSuccess) return result;
            result = await _mesRuntimeService.StartJingWangShiftAsync(_configuration);
            if (result.IsSuccess) _jwShiftIdentity = identity;
            return result;
        }
        finally { _jwShiftGate.Release(); }
    }

    private async Task StartJingWangSessionAsync()
    {
        var result = await EnsureJingWangShiftAsync();
        AddLog($"景旺 MES 启动：{result.Message}");
        RefreshDerivedState();
        if (!result.IsSuccess) await ShowRuntimePopupAsync("景旺 MES 启动失败，扫码时将重试。\n" + result.Message, true, true);
    }

    private async Task RejectJingWangScanAsync(string barcode, string reason)
    {
        _jingWangPrecheckStatus = "未过站";
        _jingWangPrecheckBarcode = string.Empty;
        ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), false);
        await RecordScanFailureAsync();
        StatusMessage = $"SN {barcode} 过站失败，已停止本次测试：{reason}";
        AddLog(StatusMessage);
        await ShowRuntimePopupAsync(StatusMessage, true, true);
    }

    private bool ConfirmJingWangOffline()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var password = new PasswordBox { Margin = new Thickness(0, 8, 0, 12) };
        var confirm = new Button { Content = "确认关闭 MES", IsDefault = true, Height = 30 };
        var dialog = new Window { Owner = Application.Current.MainWindow, Title = "关闭景旺 MES", Content = panel,
            Width = 320, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        panel.Children.Add(new TextBlock { Text = "请输入关闭 MES 的密码" });
        panel.Children.Add(password);
        panel.Children.Add(confirm);
        confirm.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => password.Focus();
        if (dialog.ShowDialog() != true) return false;
        if (password.Password == "jingwang") return true;
        _configurationDialogService.ShowAlert("密码错误", "MES 继续在线。");
        return false;
    }
}
