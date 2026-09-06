namespace Chummer.Android.Native;

/// <summary>
/// Collapses a burst of UI refresh requests into one dispatcher pass while preserving a
/// request that arrives during the active pass. This class owns scheduling state only; the
/// page remains responsible for running its renderer on the UI dispatcher.
/// </summary>
public sealed class NativeRefreshCoalescer
{
    private const long LegacyGeneration = 1;
    private PendingRefresh? _pending;
    private long _nextRequestId;
    private long _scheduledGeneration;

    private sealed record PendingRefresh(long Generation, long RequestId);

    /// <summary>
    /// Retains the newest visible-page generation that observed a change. An older event
    /// callback can never replace work already recorded for a later appearance.
    /// </summary>
    public long MarkPending(long generation)
    {
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        long requestId = Interlocked.Increment(ref _nextRequestId);
        var replacement = new PendingRefresh(generation, requestId);
        PendingRefresh? observed = Volatile.Read(ref _pending);
        while (observed is null || observed.Generation <= generation)
        {
            PendingRefresh? exchanged = Interlocked.CompareExchange(
                ref _pending,
                replacement,
                observed);
            if (ReferenceEquals(exchanged, observed))
            {
                return requestId;
            }

            observed = exchanged;
        }

        return 0;
    }

    public bool TryGetPendingRequest(long generation, out long requestId)
    {
        PendingRefresh? pending = Volatile.Read(ref _pending);
        if (pending?.Generation == generation)
        {
            requestId = pending.RequestId;
            return true;
        }

        requestId = 0;
        return false;
    }

    /// <summary>
    /// Claims dispatcher ownership only when the requested generation is still the newest
    /// pending work. Ownership is generation-tagged so an abandoned callback cannot clear a
    /// successor appearance's scheduled pass.
    /// </summary>
    public bool TrySchedulePending(long generation)
        => generation > 0
           && Volatile.Read(ref _pending)?.Generation == generation
           && Interlocked.CompareExchange(
               ref _scheduledGeneration,
               generation,
               0) == 0;

    public bool TryTakePending(long generation)
    {
        if (generation <= 0
            || Volatile.Read(ref _scheduledGeneration) != generation)
        {
            return false;
        }

        PendingRefresh? observed = Volatile.Read(ref _pending);
        while (observed?.Generation == generation)
        {
            PendingRefresh? exchanged = Interlocked.CompareExchange(
                ref _pending,
                null,
                observed);
            if (ReferenceEquals(exchanged, observed))
            {
                return true;
            }

            observed = exchanged;
        }

        return false;
    }

    public void ReleaseSchedule(long generation)
    {
        if (generation > 0)
        {
            Interlocked.CompareExchange(ref _scheduledGeneration, 0, generation);
        }
    }

    /// <summary>
    /// Records that an explicit render already observed every request through this generation.
    /// Work for a later appearance is retained.
    /// </summary>
    public void DiscardPendingThrough(long generation)
    {
        PendingRefresh? observed = Volatile.Read(ref _pending);
        while (observed is not null && observed.Generation <= generation)
        {
            PendingRefresh? exchanged = Interlocked.CompareExchange(
                ref _pending,
                null,
                observed);
            if (ReferenceEquals(exchanged, observed))
            {
                return;
            }

            observed = exchanged;
        }
    }

    /// <summary>
    /// Consumes only the exact request handed to another refresh owner. A newer request in
    /// the same appearance remains pending for its own dispatcher pass.
    /// </summary>
    public bool DiscardPending(long generation, long requestId)
    {
        if (generation <= 0 || requestId <= 0)
        {
            return false;
        }

        PendingRefresh? observed = Volatile.Read(ref _pending);
        while (observed?.Generation == generation
               && observed.RequestId == requestId)
        {
            PendingRefresh? exchanged = Interlocked.CompareExchange(
                ref _pending,
                null,
                observed);
            if (ReferenceEquals(exchanged, observed))
            {
                return true;
            }

            observed = exchanged;
        }

        return false;
    }

    /// <summary>
    /// Retires queued work and dispatcher ownership for an appearance that left the visual
    /// tree. A delayed callback keeps its old token and cannot release a newer owner.
    /// </summary>
    public void AbandonThrough(long generation)
    {
        DiscardPendingThrough(generation);
        long scheduled = Volatile.Read(ref _scheduledGeneration);
        while (scheduled != 0 && scheduled <= generation)
        {
            long exchanged = Interlocked.CompareExchange(
                ref _scheduledGeneration,
                0,
                scheduled);
            if (exchanged == scheduled)
            {
                return;
            }

            scheduled = exchanged;
        }
    }

    /// <summary>
    /// Marks the latest state as pending and returns true only to the caller that owns the
    /// next dispatcher post.
    /// </summary>
    public bool Request()
    {
        MarkPending(LegacyGeneration);
        return TrySchedulePending(LegacyGeneration);
    }

    /// <summary>
    /// Consumes every request observed before this call as one render pass.
    /// </summary>
    public bool TryTakePending()
        => TryTakePending(Volatile.Read(ref _scheduledGeneration));

    /// <summary>
    /// Releases the active dispatcher owner. If a request arrived while it was rendering,
    /// exactly one caller receives ownership of the follow-up post.
    /// </summary>
    public bool Complete(bool allowReschedule)
    {
        long generation = Volatile.Read(ref _scheduledGeneration);
        ReleaseSchedule(generation);
        return allowReschedule && TrySchedulePending(LegacyGeneration);
    }

    /// <summary>
    /// A synchronous refresh has already rendered the latest state, or the page left the
    /// visual tree. Pending work can be discarded without disturbing an in-flight owner.
    /// </summary>
    public void DiscardPending()
        => Interlocked.Exchange(ref _pending, null);
}
