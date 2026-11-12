using System.Runtime.InteropServices;
using RecoveryBLL.Interfaces;

namespace RecoveryDAL.Storage;

/// <summary>
/// Keeps Windows from sleeping while a run is busy (SetThreadExecutionState). The screen may still turn off.
/// It cannot stop a Windows Update restart; runs continue safely after one, but pausing updates is still advised.
/// </summary>
public sealed class KeepAwake : IKeepAwake
{
    [Flags]
    enum ExecutionState : uint { SystemRequired = 0x1, AwayModeRequired = 0x40, Continuous = 0x80000000 }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    // The flag belongs to the calling thread, so the count does too. Nested runs on one thread
    // (plan -> scan -> hash) keep it on until the outermost one ends. Always dispose on the same thread (using).
    [ThreadStatic] static int depth;

    public IDisposable Begin(string reason)
    {
        if (++depth == 1) SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
        return new Token();
    }

    sealed class Token : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (--depth == 0) SetThreadExecutionState(ExecutionState.Continuous);
        }
    }
}
