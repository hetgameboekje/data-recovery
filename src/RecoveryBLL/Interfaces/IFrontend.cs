using RecoveryBLL.Models;

namespace RecoveryBLL.Interfaces;

/// <summary>
/// Step 2 of every deletion: the frontend asks the person one last time, right before anything is deleted
/// (console: type DELETE; desktop app: a second dialog). Step 1 (approving the list by its code) happens before.
/// </summary>
public interface IDeletionConfirmer
{
    bool ConfirmFinal(DeletionBatch batch);
}

/// <summary>Used when no frontend registers a confirmer: nothing is ever deleted.</summary>
public sealed class RefuseAllDeletions : IDeletionConfirmer
{
    public bool ConfirmFinal(DeletionBatch batch) => false;
}

/// <summary>Keeps the PC from going to sleep while a long scan, hash or copy runs.</summary>
public interface IKeepAwake
{
    /// <summary>Dispose on the same thread to allow sleep again.</summary>
    IDisposable Begin(string reason);
}
