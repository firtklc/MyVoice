namespace MyVoice.Windows.Core;

/// <summary>
/// Runs state-machine commands strictly in order. A command whose handler produces more commands (e.g. OpenMic
/// failing → MicOpenFailed) queues them behind the rest of the current list instead of running them in the middle.
/// UI thread only.
/// </summary>
public sealed class CommandQueue(Action<Command> handle)
{
    readonly Queue<Command> _pending = new();
    bool _draining;

    public void Run(IEnumerable<Command> commands)
    {
        foreach (var command in commands) _pending.Enqueue(command);
        if (_draining) return; // the outer Run drains them, in order
        _draining = true;
        try
        {
            while (_pending.TryDequeue(out var command)) handle(command);
        }
        catch
        {
            _pending.Clear(); // the rest belonged to a list that already failed; never replay it later
            throw;
        }
        finally
        {
            _draining = false;
        }
    }
}
