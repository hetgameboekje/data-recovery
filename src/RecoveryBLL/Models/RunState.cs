namespace RecoveryBLL.Models;

/// <summary>Something a run holds that must be given back if the run dies (crash, power loss, killed process).</summary>
public enum LeaseKind
{
    /// <summary>A Linux partition attached with wsl --mount. Target = physical disk number.</summary>
    WslMount,
    /// <summary>A destination folder a copy is writing to (may hold half-written .rtpart files). Target = full path.</summary>
    CopyDestination,
}

/// <summary>One entry in the run-state journal. The owner is identified by process id + start time (ids get reused).</summary>
public sealed record RunLease(LeaseKind Kind, string Target, int ProcessId, long ProcessStartTicks, string Since);

/// <summary>What a clean-up freed, and what it could not.</summary>
public sealed record CleanupResult(
    IReadOnlyList<string> Released,
    long TempFilesDeleted,
    IReadOnlyList<string> Problems,
    IReadOnlyList<RunLease> StillHeld)
{
    /// <summary>Half-written copies found; deleting them needs the two-step check.</summary>
    public DeletionBatch Leftovers { get; init; } = DeletionBatch.Empty;
    public bool DidSomething => Released.Count > 0 || TempFilesDeleted > 0 || Problems.Count > 0 || !Leftovers.IsEmpty;
}

/// <summary>What a path is going to be used for; destinations are written to, sources only read.</summary>
public enum PathUse { ScanSource, Destination }
