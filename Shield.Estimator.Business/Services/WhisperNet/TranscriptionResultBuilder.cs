using System.Text;

namespace Shield.Estimator.Business.Services.WhisperNet;

public class TranscriptionStringBuilder
{
    private readonly StringBuilder _sb = new();
    private int? _previousChannel;
    private readonly int _totalChannels;

    public TranscriptionStringBuilder(int totalChannels)
    {
        _totalChannels = totalChannels;
    }

    public void AppendSegment(string text, int currentChannel, string? segmentStartEnd = null)
    {
        var speakerLabel = GetSpeakerLabel(currentChannel);
        var isNewSpeaker = _previousChannel != currentChannel;

        _sb.Append(isNewSpeaker ? $"\n{speakerLabel}: " : "");

        if (segmentStartEnd != null)
        {
            _sb.Append($"\n{segmentStartEnd}: ");
        }

        _sb.Append($"{text} ");
        _previousChannel = currentChannel;
    }
    private string GetSpeakerLabel(int currentChannel)
    {
        return currentChannel < _totalChannels
            ? $"Собеседник {currentChannel + 1}"
            : "Неизвестный";
    }

    public string Build() => _sb.ToString().Trim();
}
