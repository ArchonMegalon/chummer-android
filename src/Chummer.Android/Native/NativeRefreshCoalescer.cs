namespace Chummer.Android.Native;

/// <summary>
/// Collapses a burst of UI refresh requests into one dispatcher pass while preserving a
/// request that arrives during the active pass. This class owns scheduling state only; the
/// page remains responsible for running its renderer on the UI dispatcher.
/// </summary>
public sealed class NativeRefreshCoalescer
{
    private int _pending;
    private int _scheduled;

    /// <summary>
    /// Marks the latest state as pending and returns true only to the caller that owns the
    /// next dispatcher post.
    /// </summary>
    public bool Request()
    {
        Interlocked.Exchange(ref _pending, 1);
        return Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0;
    }

    /// <summary>
    /// Consumes every request observed before this call as one render pass.
    /// </summary>
    public bool TryTakePending()
        => Interlocked.Exchange(ref _pending, 0) != 0;

    /// <summary>
    /// Releases the active dispatcher owner. If a request arrived while it was rendering,
    /// exactly one caller receives ownership of the follow-up post.
    /// </summary>
    public bool Complete(bool allowReschedule)
    {
        Volatile.Write(ref _scheduled, 0);
        return allowReschedule
               && Volatile.Read(ref _pending) != 0
               && Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0;
    }

    /// <summary>
    /// A synchronous refresh has already rendered the latest state, or the page left the
    /// visual tree. Pending work can be discarded without disturbing an in-flight owner.
    /// </summary>
    public void DiscardPending()
        => Interlocked.Exchange(ref _pending, 0);
}
