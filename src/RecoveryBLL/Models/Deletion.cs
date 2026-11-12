using System.Security.Cryptography;
using System.Text;

namespace RecoveryBLL.Models;

/// <summary>One file the toolkit proposes to delete, and why it is safe.</summary>
public sealed record DeletionCandidate(string Path, long Size, string Reason);

/// <summary>
/// Every deletion needs two confirmations (two-step verification):
///   1. approve this exact list: <see cref="Code"/> is derived from the paths and sizes, so a code approved for one list
///      never deletes a different one (a changed list gives a different code);
///   2. a final confirmation right before deleting (typing DELETE in the console, a second dialog in the app).
/// The services then re-check every file just before it is removed.
/// </summary>
public sealed class DeletionBatch
{
    public IReadOnlyList<DeletionCandidate> Files { get; }
    public string Code { get; }
    public long Count => Files.Count;
    public long Bytes => Files.Sum(f => f.Size);
    public bool IsEmpty => Files.Count == 0;

    public DeletionBatch(IEnumerable<DeletionCandidate> files)
    {
        Files = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var text = string.Join("\n", Files.Select(f => f.Path.ToUpperInvariant() + "|" + f.Size));
        Code = Files.Count == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..6];
    }

    public static readonly DeletionBatch Empty = new([]);

    /// <summary>Step 1: the code the user gave must be the code of this (freshly computed) list.</summary>
    public bool IsApprovedBy(string? code) =>
        !IsEmpty && !string.IsNullOrWhiteSpace(code) && string.Equals(code.Trim(), Code, StringComparison.OrdinalIgnoreCase);
}
