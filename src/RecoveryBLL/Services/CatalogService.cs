using RecoveryBLL.Interfaces;

namespace RecoveryBLL.Services;

/// <summary>Opens the configured catalog database and makes sure its tables exist.</summary>
public sealed class CatalogService(ICatalogStore store)
{
    public string Description => store.Description;

    public void Initialize() => RepositoryGuard.Run(store.EnsureCreated, "Cannot open the catalog database");
}
