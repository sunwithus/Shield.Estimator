// TodoItem.cs

using System.Collections.Concurrent;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;

public class TodoItem
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public bool IsDone { get; set; } = false;
    public bool IsRunning { get; set; } = false;
    public bool IsRunPressed { get; set; } = false;
    public bool IsStopPressed { get; set; } = false;
    public int CompletedLanguageDetect { get; set; } = 0;
    public int CompletedKeys { get; set; } = 0;
    public int ProcessedWhisper { get; set; } = 0;
    public int TotalKeys { get; set; } = 0;
    public string? ProcessingMessage { get; set; } = "";
    public bool IsCyclic { get; set; } = true;
    public int BackLight { get; set; } = 0;
    public DateTime StartDateTime { get; set; } = DateTime.Now.AddMonths(-1);
    public DateTime EndDateTime { get; set; } = DateTime.Now.AddMonths(1).AddYears(1);
    public int MoreThenDuration { get; set; } = 10;
    public string? DbType { get; set; } = "Oracle";
    public string? User { get; set; } = "SYSDBA";
    public string? Password { get; set; } = "masterkey";
    public string? ServerAddress { get; set; } = "127.0.0.1";
    public string? Scheme { get; set; } = "";
    public string? LastError { get; set; } = "";
    public string? Statistic { get; set; }


    public bool IsExecutionTime { get; set; } = false;
    public TimeSpan? StartExecutionTime { get; set; } = new TimeSpan(20, 00, 00);
    public TimeSpan? EndExecutionTime { get; set; } = new TimeSpan(08, 00, 00);

    public ConcurrentDictionary<string, int> LanguageCounts { get; set; } = new ConcurrentDictionary<string, int>();
    public ConcurrentDictionary<string, string> LanguageNames { get; set; } = new ConcurrentDictionary<string, string>();

    public void ResetCounters()
    {
        this.CompletedKeys = 0;
        this.CompletedLanguageDetect = 0;
        this.ProcessedWhisper = 0;
        this.TotalKeys = 0;
        this.Statistic = "";
    }
}
