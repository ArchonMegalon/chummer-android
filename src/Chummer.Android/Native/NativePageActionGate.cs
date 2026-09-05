namespace Chummer.Android.Native;

/// <summary>
/// Gives one visible page action exclusive ownership of the interaction surface. Android may
/// deliver multiple click callbacks before a long-running handler has rendered its next state;
/// rejecting every later claim prevents duplicate navigation and overlapping mutations.
/// </summary>
public sealed class NativePageActionGate
{
    private int _claimed;

    public bool IsClaimed => Volatile.Read(ref _claimed) != 0;

    public bool TryClaim()
        => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

    public void Release()
    {
        if (Interlocked.Exchange(ref _claimed, 0) == 0)
        {
            throw new InvalidOperationException("A page action cannot be released without a claim.");
        }
    }
}
