using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Whisper.net.Logger;

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

    public void AppendSegment(string text, int currentChannel)
    {
       
        if (_previousChannel == currentChannel)
        {
            _sb.Append($"{text} ");
        }
        else
        {
            var speakerLabel = currentChannel < _totalChannels
                ? $"Собеседник {currentChannel + 1}"
                : "Неизвестный";
            _sb.Append($"\n{speakerLabel}: {text} ");
        }
        _previousChannel = currentChannel;
    }

    public string Build() => _sb.ToString().Trim();
}
