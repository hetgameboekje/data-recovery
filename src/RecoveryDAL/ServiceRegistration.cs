using Microsoft.Extensions.DependencyInjection;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;
using RecoveryDAL.Sqlite;
using RecoveryDAL.SqlServer;
using RecoveryDAL.Storage;
using RecoveryDAL.Xml;

namespace RecoveryDAL;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers the DAL implementations of the BLL interfaces. The database provider comes from the .env
    /// (DB_PROVIDER), so switching SQLite -> SQL Server changes no code outside this method.
    /// </summary>
    public static IServiceCollection AddRecoveryDal(this IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton(settings);

        switch (settings.Provider)
        {
            case DatabaseProvider.Sqlite:
                services.AddSingleton(new SqliteDb(settings.ConnectionString));
                services.AddSingleton<ICatalogStore>(sp => sp.GetRequiredService<SqliteDb>());
                services.AddTransient<IDiskRepository, SqliteDiskRepository>();
                services.AddTransient<IFileRepository, SqliteFileRepository>();
                services.AddTransient<IReportRepository, SqliteReportRepository>();
                services.AddTransient<ICatalogExchange, SqliteCatalogExchange>();
                break;
            case DatabaseProvider.SqlServer:
                services.AddSingleton(new SqlServerDb(settings.ConnectionString));
                services.AddSingleton<ICatalogStore>(sp => sp.GetRequiredService<SqlServerDb>());
                services.AddTransient<IDiskRepository, SqlServerDiskRepository>();
                services.AddTransient<IFileRepository, SqlServerFileRepository>();
                services.AddTransient<IReportRepository, SqlServerReportRepository>();
                services.AddTransient<ICatalogExchange, SqlServerCatalogExchange>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Provider, "Unknown database provider");
        }

        services.AddTransient<ICatalogSourceFactory, SqliteCatalogSourceFactory>();
        services.AddSingleton<IDictionaryRepository>(new XmlDictionaryRepository(settings.DictionariesFile));
        services.AddSingleton<IVolumeProvider, WindowsVolumeProvider>();
        services.AddSingleton<IPhysicalDeviceProvider, WindowsPhysicalDeviceProvider>();
        services.AddSingleton<IFileSystemWalker, FileSystemWalker>();
        services.AddSingleton<IFileContentReader, FileContentReader>();
        services.AddSingleton<IWslGateway, WslGateway>();
        services.AddSingleton<IElevationInfo, WindowsElevationInfo>();
        return services;
    }
}
