using Chummer.Blazor.Services;

namespace Chummer.Android.Platform;

public sealed class AndroidDocumentInbox : IWorkbenchExternalDocumentInbox
{
    private readonly object _gate = new();
    private readonly Queue<WorkbenchExternalDocument> _pending = new();

    public event EventHandler? DocumentAvailable;

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    public void Add(AndroidDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            _pending.Enqueue(new WorkbenchExternalDocument(document.DisplayName, document.Content));
        }

        DocumentAvailable?.Invoke(this, EventArgs.Empty);
    }

    public bool TryTake(out WorkbenchExternalDocument? document)
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                document = null;
                return false;
            }

            document = _pending.Dequeue();
            return true;
        }
    }
}
