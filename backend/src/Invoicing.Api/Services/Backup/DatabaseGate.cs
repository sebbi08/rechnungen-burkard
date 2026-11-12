namespace Burkard.Invoicing.Api.Services.Backup;

/// <summary>Stands in front of every use of the database. A restore closes it:
/// nobody new gets in, and the restore waits until everyone inside has left
/// before it touches the file. One closer at a time; a second one waits until
/// the first has opened it again.</summary>
public sealed class DatabaseGate
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _closer = new(1, 1);
    private int _inside;
    private bool _closed;
    private TaskCompletionSource? _drained;

    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _closed;
            }
        }
    }

    /// <summary>Enters unless the gate is closed. Every successful entry must be
    /// paired with an <see cref="Exit"/>.</summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_closed)
            {
                return false;
            }

            _inside++;
            return true;
        }
    }

    public void Exit()
    {
        lock (_lock)
        {
            _inside--;
            if (_inside == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    /// <summary>Refuses new entries, then waits until nobody is inside. Cancelled
    /// while waiting, it opens the gate again and throws.</summary>
    public async Task CloseAsync(CancellationToken ct)
    {
        await _closer.WaitAsync(ct);
        Task drained;
        lock (_lock)
        {
            _closed = true;
            if (_inside == 0)
            {
                return;
            }

            _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _drained.Task;
        }

        try
        {
            await drained.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Open();
            throw;
        }
    }

    /// <summary>Lets requests in again. Does nothing on an open gate.</summary>
    public void Open()
    {
        lock (_lock)
        {
            if (!_closed)
            {
                return;
            }

            _closed = false;
            _drained = null;
        }

        _closer.Release();
    }
}
