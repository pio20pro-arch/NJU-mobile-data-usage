using NjuTrayApp.Services;
using NjuTrayApp.UI;

namespace NjuTrayApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var logger = new FileLogger();
        var configService = new ConfigService(logger);
        var authService = new AuthService(logger);
        var apiClient = new NjuApiClient(logger);
        var autostartService = new AutostartService();
        var appContext = new TrayApplicationContext(configService, authService, apiClient, autostartService, logger);

        Application.Run(appContext);
    }
}
