using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Services;

namespace RecoveryBLL;

public static class ServiceRegistration
{
    /// <summary>Registers the business services. The DAL registers the interfaces they depend on.</summary>
    public static IServiceCollection AddRecoveryBll(this IServiceCollection services)
    {
        services.AddSingleton<DictionaryService>();
        services.AddSingleton<DiskService>();   // singleton: remembers WSL mount roots for the session
        services.AddTransient<CatalogService>();
        services.AddTransient<ScanService>();
        services.AddTransient<FingerprintService>();
        services.AddTransient<ReportService>();
        services.AddTransient<MergeService>();
        services.AddTransient<LinuxDiskService>();
        return services;
    }
}
