//ReplSingleton.cs

using System.ComponentModel;

namespace Shield.Estimator.Shared.Components.Modules.TranslateFiles;

public class TranslateSingleton
{
    private static TranslateSingleton instance;
    private static readonly object padlock = new object();

    private TranslateSingleton()
    {
    }

    public static TranslateSingleton TranslateInstance
    {
        get
        {
            lock (padlock)
            {
                if (instance == null)
                {
                    instance = new TranslateSingleton();
                }
                return instance;
            }
        }
    }

    private bool isStopTranlation = true;
    public bool IsStopTranlation
    {
        get { return isStopTranlation; }
        set
        {
            isStopTranlation = value;
            OnPropertyChanged(nameof(IsStopTranlation));
        }
    }

    private bool isTranscribing = false;
    public bool IsTranscribing
    {
        get { return isTranscribing; }
        set
        {
            isTranscribing = value;
            OnPropertyChanged(nameof(IsTranscribing));
        }
    }

    private string processTranlation = "";
    public string ProcessTranlation
    {
        get { return processTranlation; }
        set
        {
            processTranlation = value;
            OnPropertyChanged(nameof(ProcessTranlation));
        }
    }

    public event PropertyChangedEventHandler TranslatePropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        TranslatePropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
