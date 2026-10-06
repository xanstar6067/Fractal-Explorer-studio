using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>A local 0.5 ms GPU-fence backoff, without changing Windows timer resolution.</summary>
internal sealed class HighResolutionWaiter : WaitHandle
{
    public HighResolutionWaiter() => SafeWaitHandle = new SafeWaitHandle(
        CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x100002), true);

    public void Pause()
    {
        long dueTime = -5000; // Relative delay in 100 ns units.
        if (!SafeWaitHandle.IsInvalid && SetWaitableTimer(SafeWaitHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            WaitOne();
        else Thread.Sleep(1); // Older Windows/device failure: a safe, slower fallback.
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr callback, IntPtr state, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
