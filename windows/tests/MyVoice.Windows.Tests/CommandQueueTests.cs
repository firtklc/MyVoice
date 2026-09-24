using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

public class CommandQueueTests
{
    [Fact]
    public void RunsCommandsInOrder()
    {
        var ran = new List<Command>();
        new CommandQueue(ran.Add).Run([new OpenMic(1), new RegisterEsc()]);
        Assert.Equal([new OpenMic(1), new RegisterEsc()], ran);
    }

    [Fact]
    public void CommandsRaisedWhileHandlingRunAfterTheCurrentList()
    {
        // Code review finding: OpenMic failing ran MicOpenFailed's UnregisterEsc before the RegisterEsc that
        // followed OpenMic, so Esc stayed registered — and swallowed — while the app was Ready.
        var ran = new List<Command>();
        CommandQueue? queue = null;
        queue = new CommandQueue(command =>
        {
            ran.Add(command);
            if (command is OpenMic) queue!.Run([new UnregisterEsc(), new Notify("No microphone found", NoteKind.Warning)]);
        });
        queue.Run([new OpenMic(1), new RegisterEsc()]);
        Assert.Equal([new OpenMic(1), new RegisterEsc(), new UnregisterEsc(), new Notify("No microphone found", NoteKind.Warning)], ran);
    }

    [Fact]
    public void AFailingHandlerDropsTheRestAndTheQueueKeepsWorking()
    {
        var ran = new List<Command>();
        var queue = new CommandQueue(command =>
        {
            if (command is OpenMic) throw new InvalidOperationException("boom");
            ran.Add(command);
        });
        Assert.Throws<InvalidOperationException>(() => queue.Run([new OpenMic(1), new RegisterEsc()]));
        queue.Run([new PlaySound(Sound.Cancel)]);
        Assert.Equal([new PlaySound(Sound.Cancel)], ran); // the stale RegisterEsc did not leak into the next run
    }
}
