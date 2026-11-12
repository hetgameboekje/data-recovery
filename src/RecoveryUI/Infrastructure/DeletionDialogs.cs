using RecoveryBLL;
using RecoveryBLL.Interfaces;
using RecoveryBLL.Models;

namespace RecoveryUI.Infrastructure;

/// <summary>
/// The two steps of every deletion in the desktop app, as two separate dialogs that both default to "No":
///   step 1 <see cref="ApproveList"/> shows exactly what would be deleted;
///   step 2 <see cref="ConfirmFinal"/> (called by the services right before deleting) asks once more.
/// </summary>
sealed class DeletionDialogs : IDeletionConfirmer
{
    public static bool ApproveList(DeletionBatch batch, string intro) =>
        MessageBox.Show($"{intro}\n\n{List(batch)}\n\nStep 1 of 2: approve deleting exactly these {batch.Count:N0} files ({Format.Bytes(batch.Bytes)})?",
            "Delete files? (step 1 of 2)", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Called from the copy's worker thread: shown on the UI thread, owned by the main window.</summary>
    public bool ConfirmFinal(DeletionBatch batch)
    {
        var owner = Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (owner is { IsHandleCreated: true, InvokeRequired: true }) return owner.Invoke(() => AskFinal(owner, batch));
        return AskFinal(owner, batch);
    }

    static bool AskFinal(IWin32Window? owner, DeletionBatch batch) =>
        MessageBox.Show(owner, $"Step 2 of 2: permanently delete {batch.Count:N0} files ({Format.Bytes(batch.Bytes)})?\n\nThis cannot be undone.",
            "Delete files? (step 2 of 2)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    static string List(DeletionBatch batch, int max = 12)
    {
        var lines = batch.Files.Take(max).Select(f => $"{Format.Bytes(f.Size),10}  {f.Path}").ToList();
        if (batch.Count > max) lines.Add($"... and {batch.Count - max:N0} more");
        return string.Join("\n", lines);
    }
}
