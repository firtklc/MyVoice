using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

public class TranscriptTextTests
{
    [Fact]
    public void JoinsSegmentsOnOneLine() =>
        // Whisper segments start with a space; the Python spike showed newlines between segments.
        Assert.Equal("Hey Cloud, can you check? I want the same hotkey.",
            TranscriptText.Join([" Hey Cloud, can you check?", "\n I want the same hotkey."]));

    [Fact] public void TrimsAndCollapsesWhitespace() => Assert.Equal("one two three", TranscriptText.Join(["  one  ", " two\t\tthree \r\n"]));

    [Fact] public void NoSegmentsIsEmpty() => Assert.Equal("", TranscriptText.Join([]));

    [Fact] public void BlankAudioMarkerIsEmpty() => Assert.Equal("", TranscriptText.Join([" [BLANK_AUDIO]"]));

    [Fact] public void BlankAudioMarkerIsRemovedFromRealText() => Assert.Equal("Hello there", TranscriptText.Join([" Hello", " [BLANK_AUDIO]", " there"]));
}
