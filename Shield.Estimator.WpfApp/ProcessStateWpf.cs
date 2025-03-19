//ProcessStateWpf.cs 

using System.ComponentModel;

namespace Shield.Estimator.Wpf;

public class ProcessStateWpf : INotifyPropertyChanged
{
    private string _consoleMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isProcessing;

    public string ConsoleMessage
    {
        get => _consoleMessage;
        set
        {
            _consoleMessage = value;
            OnPropertyChanged(nameof(ConsoleMessage));
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            _isProcessing = value;
            OnPropertyChanged(nameof(IsProcessing));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
