using BW_V01.App.ViewModels;
using BW_V01.Core.Communication;
using BW_V01.Core.Configuration;

namespace BW_V01.App.Services;

public interface IConfigurationDialogService
{
    bool ShowBasicConfiguration(AppConfiguration configuration);

    bool ShowCommunicationConfiguration(AppConfiguration configuration, CommunicationTestService communicationTestService);

    bool ShowMesConfiguration(AppConfiguration configuration);

    bool ShowTestItemConfiguration(TestItemConfigurationSet configuration);

    bool ShowUserConfiguration(UserConfiguration configuration);

    bool ShowProjectProfileImport(IReadOnlyList<ProjectProfile> profiles, out ProjectProfile? selectedProfile);

    bool ShowLogin(AuthenticationService authenticationService, UserConfiguration configuration);

    void ShowDataAnalysis(IEnumerable<ProductionRecordViewModel> records, int passCount, int failCount, int currentConsecutiveNg);

    void ShowHelp(AppConfiguration configuration);

    void ShowAlert(string title, string message);
}
