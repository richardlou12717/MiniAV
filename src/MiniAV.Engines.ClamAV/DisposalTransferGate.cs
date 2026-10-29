namespace MiniAV.Engines.ClamAV;

/// <summary>
/// Scope guard: a resource is normally disposed on exit, unless ownership is explicitly
/// transferred to a reader-completion continuation. No change to the resource itself.
/// </summary>
internal sealed class DisposalTransferGate<T> : IDisposable where T : IDisposable
{
    internal T Value { get; }
    private int _transferred;

    internal DisposalTransferGate(T value) => Value = value;

    internal void Transfer() => Interlocked.Exchange(ref _transferred, 1);

    public void Dispose()
    {
        if (Volatile.Read(ref _transferred) == 0)
            Value.Dispose();
    }
}
