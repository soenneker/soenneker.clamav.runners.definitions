using Microsoft.Extensions.DependencyInjection;
using Soenneker.Clamav.Freshclam.Util.Registrars;
using Soenneker.Clamav.Runners.Definitions.Utils;
using Soenneker.Clamav.Runners.Definitions.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.NuGet.Registrars;

namespace Soenneker.Clamav.Runners.Definitions;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.SetupIoC();
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddDirectoryUtilAsSingleton()
                .AddFileUtilAsSingleton()
                .AddNuGetUtilAsSingleton()
                .AddFreshclamUtilAsSingleton()
                .AddRunnersManagerAsSingleton();

        return services;
    }
}
