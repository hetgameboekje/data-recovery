using RecoveryBLL.Exceptions;

namespace RecoveryBLL.Services;

/// <summary>Translates DAL failures into service exceptions with a readable message (same pattern in every service).</summary>
internal static class RepositoryGuard
{
    public static T Run<T>(Func<T> action, string whatFailed)
    {
        try { return action(); }
        catch (RepositoryException ex) { throw new RecoveryServiceException($"{whatFailed}: {ex.Message}", ex); }
    }

    public static void Run(Action action, string whatFailed)
    {
        try { action(); }
        catch (RepositoryException ex) { throw new RecoveryServiceException($"{whatFailed}: {ex.Message}", ex); }
    }

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
