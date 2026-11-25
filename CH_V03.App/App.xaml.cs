using System.Windows;
using CH_V03.App.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CH_V03.App.Services;
using CH_V03.App.Views;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;
using CH_V03.App.Localization;

namespace CH_V03.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        UiLanguage.Register();
        JiaLianYiInputFilter.Register();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var waitIndex = Array.IndexOf(e.Args, "--await-process");
            if (waitIndex >= 0 && waitIndex + 1 < e.Args.Length && int.TryParse(e.Args[waitIndex + 1], out var oldPid))
            {
                Process? previous = null;
                try { previous = Process.GetProcessById(oldPid); }
                catch (ArgumentException) { /* Previous process has already exited. */ }
                if (previous is not null)
                {
                    using (previous)
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        await previous.WaitForExitAsync(timeout.Token);
                }
            }
            await StartModuleAsync(e.Args.Contains("--login", StringComparer.Ordinal));
        }
        catch (Exception exception)
        {
            MessageBox.Show($"软件启动未完成：{exception.GetBaseException().Message}", "CeHao", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartModuleAsync(bool forceLogin)
    {
        var configuration = new CH_V03.Core.Configuration.ConfigurationStore(CH_V03.App.Services.AppPaths.ConfigurationFile).Load();
        if (!forceLogin && configuration.DefaultOperatorLogin)
            UiLanguage.SetForUser(configuration.AutoLoginUserName);
        var updateService = new CH_V03.App.Services.UpdateService();
        var update = await updateService.CheckAsync(configuration.Update, CancellationToken.None);
        if (update.IsAvailable)
        {
            var updatePrompt = UiLanguage.IsEnglish
                ? $"A new version {update.Manifest!.Version} is available.\n\n{update.Manifest.Notes}\n\nDownload and install it now?"
                : $"发现新版本 {update.Manifest!.Version}。\n\n{update.Manifest.Notes}\n\n现在下载并安装吗？";
            var answer = MessageBox.Show(updatePrompt,
                UiLanguage.Text("CeHao 软件更新"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    await updateService.DownloadAndStartUpdaterAsync(update.Manifest!, AppContext.BaseDirectory, CancellationToken.None);
                    Shutdown();
                    return;
                }
                catch (Exception exception)
                {
                    var updateError = UiLanguage.IsEnglish
                        ? $"The update did not finish. You can continue using the current version.\n\n{exception.Message}"
                        : $"更新没有完成，当前版本仍可正常使用。\n\n{exception.Message}";
                    MessageBox.Show(updateError, UiLanguage.Text("CeHao 软件更新"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        var gasStore = new AirtightnessStore(AppPaths.AirtightnessDirectory);
        var catalog = AirtightnessStore.BuildLoginCatalog(configuration.User, gasStore.LoadAccounts());
        var authentication = new AuthenticationService();
        var lastModule = File.Exists(AppPaths.LoginSelectionFile)
            ? JsonSerializer.Deserialize<TestModule>(File.ReadAllText(AppPaths.LoginSelectionFile))
            : TestModule.Electrical;
        var preferJingWang = configuration.User.PreferJingWangLogin || string.Equals(configuration.User.LoginJobNo, "jingwang", StringComparison.OrdinalIgnoreCase);
        var automatic = !preferJingWang && !forceLogin && lastModule == TestModule.Electrical && configuration.DefaultOperatorLogin;
        if (automatic)
        {
            var electricalAccounts = new UserConfiguration { Accounts = catalog.Accounts.Where(a => a.Module == TestModule.Electrical).ToList() };
            var userName = string.IsNullOrWhiteSpace(configuration.AutoLoginUserName) ? "operator" : configuration.AutoLoginUserName;
            automatic = authentication.LoginAsConfiguredUser(electricalAccounts, userName, out _);
        }
        if (!automatic)
        {
            var loginModel = new LoginViewModel(authentication, catalog) { UserName = preferJingWang ? "jingwang" : string.Empty };
            var login = new LoginWindow { DataContext = loginModel };
            loginModel.RequestClose += (_, result) => login.DialogResult = result;
            if (login.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        AirtightnessStore.WriteAtomic(AppPaths.LoginSelectionFile, authentication.CurrentSession.Module);
        UiLanguage.SetForUser(authentication.CurrentSession.UserName);
        JiaLianYiInputFilter.Enabled = authentication.CurrentSession.IsJiaLianYiAccount;
        Window mainWindow;
        if (authentication.CurrentSession.Module == TestModule.Airtightness)
        {
            configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration = false;
            new ConfigurationStore(AppPaths.ConfigurationFile).Save(configuration);
            mainWindow = new AirtightnessWindow(authentication.CurrentSession, gasStore);
        }
        else
        {
            configuration.User.PreferJingWangLogin = preferJingWang || string.Equals(authentication.CurrentSession.UserName, "jingwang", StringComparison.OrdinalIgnoreCase);
            configuration.User.LoginJobNo = authentication.CurrentSession.UserName;
            authentication.ApplyAccountMode(configuration);
            if (string.Equals(authentication.CurrentSession.UserName, "huaqin", StringComparison.OrdinalIgnoreCase))
            {
                configuration.AutoLoginUserName = "huaqin";
                configuration.DefaultOperatorLogin = true;
            }
            new ConfigurationStore(AppPaths.ConfigurationFile).Save(configuration);
            mainWindow = new MainWindow(authentication);
        }
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    public static void RestartForLogin()
    {
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "CeHao.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到 CeHao.exe，请从完整软件目录启动。", executable);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--login");
            start.ArgumentList.Add("--await-process");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var next = Process.Start(start) ?? throw new InvalidOperationException("无法重新打开登录窗口。");
            Current.Shutdown();
        }
        catch (Exception exception)
        {
            MessageBox.Show(UiLanguage.Text("切换登录失败：") + exception.Message, "CeHao", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
