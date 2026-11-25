using CH_V03.App.ViewModels;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Services;

public interface IConfigurationDialogService
{
    bool ShowBasicConfiguration(AppConfiguration configuration, bool canManageAllSettings);

    bool ShowKunshanJiaLianYiBasicConfiguration(AppConfiguration configuration, KunshanJiaLianYiRuntimeActions runtimeActions);

    bool ShowCommunicationConfiguration(AppConfiguration configuration, CommunicationTestService communicationTestService);

    bool ShowEndoscope(AppConfiguration configuration);

    bool ShowMesConfiguration(AppConfiguration configuration);

    void ShowMesDebugTool(AppConfiguration configuration);

    bool ShowPluginConfiguration(AppConfiguration configuration);

    bool ShowKunshanJiaLianYiConfiguration(AppConfiguration configuration);

    bool ShowTestItemConfiguration(TestItemConfigurationSet configuration);

    bool ShowUserConfiguration(UserConfiguration configuration, CompatibilityConfiguration compatibility, bool canManageAccounts);

    bool ShowKunshanJiaLianYiUserConfiguration(UserConfiguration configuration, CompatibilityConfiguration compatibility);

    bool ShowProjectProfileImport(IReadOnlyList<ProjectProfile> profiles, out ProjectProfile? selectedProfile);

    bool ShowLogin(AuthenticationService authenticationService, UserConfiguration configuration);

    void ShowDataAnalysis(IEnumerable<ProductionRecordViewModel> records, int passCount, int failCount, int currentConsecutiveNg);

    void ShowTestTrendAnalysis(IEnumerable<ProductionRecordViewModel> records);

    void ShowHelp(AppConfiguration configuration);

    void ShowAlert(string title, string message);
}
