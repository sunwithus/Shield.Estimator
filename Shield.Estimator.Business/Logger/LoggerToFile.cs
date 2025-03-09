using System.Text;
using Shield.Estimator.Business.Exceptions;


namespace Shield.Estimator.Business.Logger;
//для логирования сообщений в файл асинхронно, используя очередь
public class LoggerToFile : ILogger
{
    private string _logPath;
    private long _maxLogSizeBytes;
    private int _maxLogAgeDays;
    private Queue<string> _queue;

    delegate void QueueAddedItemEventHandler();

    event QueueAddedItemEventHandler QueueAddedItem;

    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="path">
    /// Путь к лог-файлу. Пример: @".\Logs\MainLog.txt" 
    /// (относительный путь, создаст папку Logs в директории приложения)
    /// </param>
    /// <param name="maxLogSizeBytes">Максимальный размер файла в байтах (0 - без ограничений)</param>
    public LoggerToFile(string path, long maxLogSizeBytes = 1048576, int maxLogAgeDays = 15)
    {
        _logPath = Path.GetFullPath(path);
        _maxLogAgeDays = maxLogAgeDays;
        _maxLogSizeBytes = maxLogSizeBytes;

        if (!Directory.Exists(Path.GetDirectoryName(_logPath)))
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
        _queue = new Queue<string>();
        QueueAddedItem += WriteQueue;

        CleanOldLogs();
    }

    /// <summary>
    /// Добавление информационных сообщений в файл лога приложения
    /// </summary>
    /// <param name="message"></param>
    public async Task AddLogMessage(string message)
    {
        await Task.Run(() => _queue.Enqueue(message));
        OnQueueAddedItem();
    }

    /// <summary>
    /// Запись сообщений в лог
    /// </summary>
    private void WriteQueue()
    {
        if (string.IsNullOrEmpty(_logPath))
            return;

        try
        {
            while (_queue.Count > 0)
            {
                var message = _queue.Dequeue();
                lock (_logPath)
                {
                    // Проверка размера файла перед записью
                    if (_maxLogSizeBytes > 0 && File.Exists(_logPath))
                    {
                        var fileInfo = new FileInfo(_logPath);
                        if (fileInfo.Length > _maxLogSizeBytes)
                        {
                            RotateLogs();
                        }
                    }

                    File.AppendAllLines(_logPath,
                        new[] { $@"{DateTime.Now:dd\.MM\.yyyy HH:mm:ss} [{Thread.CurrentThread.ManagedThreadId}]: {message}" }, Encoding.Default);
                }
            }
        }
        catch (Exception /*ex*/)
        {
            //throw new FailedLoggerToFileException($"Ошибка записи лога: {ex.Message}", ex);
        }
    }

    private void RotateLogs()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var newPath = Path.Combine(
                Path.GetDirectoryName(_logPath),
                $"{Path.GetFileNameWithoutExtension(_logPath)}_{timestamp}{Path.GetExtension(_logPath)}"
            );
            File.Move(_logPath, newPath);
        }
        catch (Exception ex)
        {
            throw new FailedLoggerToFileException($"Ошибка ротации логов: {ex.Message}", ex);
        }
    }

    private void CleanOldLogs()
    {
        if (_maxLogAgeDays <= 0) return;

        try
        {
            var directory = Path.GetDirectoryName(_logPath);
            var logFiles = Directory.GetFiles(directory, "*.txt");
            if (logFiles.Count() <= 1) return;

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-_maxLogAgeDays))
                {
                    fileInfo.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            throw new FailedLoggerToFileException($"Ошибка очистки логов: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Оповещение о поступлении сообщений для записи в файл
    /// </summary>
    private void OnQueueAddedItem()
    {
        QueueAddedItem?.Invoke();
    }
}
