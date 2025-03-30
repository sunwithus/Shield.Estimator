//ProcessStateWpf.cs 

using System.ComponentModel;
using System.Diagnostics;
using System.Management;

namespace Shield.Estimator.Wpf;

public class ProcessStateWpf : INotifyPropertyChanged
{
    private string _consoleMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isProcessing;

    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _ramCounter;
    private float _cpuUsage;
    private float _ramUsage;

    public ProcessStateWpf()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
    }

    public float CpuUsage
    {
        get => _cpuUsage;
        set { _cpuUsage = value; OnPropertyChanged(nameof(CpuUsage)); }
    }

    public float RamUsage
    {
        get => _ramUsage;
        set { _ramUsage = value; OnPropertyChanged(nameof(RamUsage)); }
    }

    public void UpdateMetrics()
    {
        CpuUsage = _cpuCounter.NextValue();
        RamUsage = 100 - (_ramCounter.NextValue() / GetTotalMemory() * 100);
    }

    private float GetTotalMemory()
    {
        using var mc = new ManagementClass("Win32_ComputerSystem");
        foreach (ManagementObject mo in mc.GetInstances())
        {
            return (float)(Convert.ToDouble(mo["TotalPhysicalMemory"]) / (1024 * 1024));
        }
        return 0;
    }


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
