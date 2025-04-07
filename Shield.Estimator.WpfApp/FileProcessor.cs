//FileProcessor.cs

using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Services.WhisperNet;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options.WhisperOptions;

namespace Shield.Estimator.Wpf;

/// <summary>
/// Класс для обработки аудиофайлов и их транскрибирования с помощью WhisperNet.
/// </summary>
public class FileProcessor
{
    private readonly WhisperNetService _whisperService;
    private readonly ILogger<FileProcessor> _logger;
    private CancellationTokenSource _cts;
    private readonly IOptions<WhisperNetOptions> _options;

    private readonly ConcurrentQueue<string> _fileQueue = new();
    private readonly SemaphoreSlim _processingSemaphore;
    private readonly HashSet<string> _processedFiles = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Конструктор класса FileProcessor.
    /// </summary>
    /// <param name="whisperService">Сервис для транскрибирования аудио.</param>
    /// <param name="logger">Логгер для записи ошибок и информации.</param>
    public FileProcessor(
        WhisperNetService whisperService,
        ILogger<FileProcessor> logger,
        IOptions<WhisperNetOptions> options)
    {
        _whisperService = whisperService;
        _logger = logger;
        _options = options;
        _processingSemaphore = new SemaphoreSlim(_options.Value.MaxConcurrentTasks, _options.Value.MaxConcurrentTasks);
    }

    /// <summary>
    /// Асинхронно обрабатывает существующие аудиофайлы в указанной директории.
    /// </summary>
    /// <param name="inputPath">Путь к входной директории с аудиофайлами.</param>
    /// <param name="outputPath">Путь к выходной директории для обработанных файлов.</param>
    /// <param name="state">Объект для отслеживания состояния процесса.</param>
    public async Task ProcessExistingFilesAsync(string inputPath, string outputPath, string selectedModel, ProcessStateWpf state)
    {
        _cts = new CancellationTokenSource();
        var mediaExtensions = GetSupportedMediaExtensions();
        var processingTask = Task.Run(() => ProcessQueueAsync(outputPath, selectedModel, state), _cts.Token);

        while (!_cts.Token.IsCancellationRequested)
        {
            var files = Directory.GetFiles(inputPath, "*.*")
                .Where(file => !IsEnqueuedOrProcessed(file))
                .ToList();

            foreach (var file in files)
            {
                if (IsSupportedFile(file, mediaExtensions))
                {
                    EnqueueFile(file);
                    UpdateConsole(state, $"Добавлен в очередь: {file}", true);
                }

            }
            if(!_processedFiles.Any())
            {
                UpdateConsole(state, $"Ожидание новых файлов...", true);
            }
            
            await Task.Delay(TimeSpan.FromSeconds(11), _cts.Token); // Уменьшено время задержки
        }
        await processingTask; // Ожидаем завершения только при отмене
    }

    private void EnqueueFile(string filePath)
    {
        lock (_syncRoot)
        {
            if (_processedFiles.Contains(filePath)) return;
            _fileQueue.Enqueue(filePath);
            _processedFiles.Add(filePath);
        }
    }
    private HashSet<string> GetSupportedMediaExtensions() => new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".aac", ".flac", ".m4a", ".wma", ".aiff", ".alac",
        ".mp4", ".avi", ".mov", ".mkv", ".flv", ".wmv", ".webm", ".mpeg", ".3gp",
        ".m4v", ".vob", ".ogg", ".mts", ".m2ts", ".ts"
    };

    private bool IsSupportedFile(string file, HashSet<string> extensions) =>
    extensions.Contains(Path.GetExtension(file));

    private bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void MoveToErrorFolder(string filePath, string outputPath)
    {
        try
        {
            var errorPath = Path.Combine(outputPath, "Errors");
            Directory.CreateDirectory(errorPath);
            File.Move(filePath, Path.Combine(errorPath, Path.GetFileName(filePath)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка перемещения файла в папку ошибок");
        }
    }

    private async Task ProcessQueueAsync(string outputPath, string selectedModel, ProcessStateWpf state)
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            if (_fileQueue.TryDequeue(out var filePath))
            {
                await _processingSemaphore.WaitAsync(_cts.Token);

                // Запускаем обработку файла в отдельной задаче без ожидания
                _ = ProcessFileWithRetryAsync(filePath, outputPath, selectedModel, state, 3)
                    .ContinueWith(_ => _processingSemaphore.Release());
            }
            else
            {
                await Task.Delay(500, _cts.Token);
            }
        }
    }

    private async Task ProcessFileWithRetryAsync(
    string filePath,
    string outputPath,
    string selectedModel,
    ProcessStateWpf state,
    int maxRetries)
    {
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                if (IsFileLocked(filePath) || new FileInfo(filePath).Length == 0)
                {
                    await Task.Delay(1000 * (retry + 1), _cts.Token);
                    continue;
                }

                await ProcessAudioFileAsync(filePath, outputPath, selectedModel, state, _cts.Token);
                MarkAsProcessed(filePath);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки файла {FilePath} (попытка {Retry})", filePath, retry + 1);
                if (retry == maxRetries - 1)
                    MoveToErrorFolder(filePath, outputPath);
            }
        }
    }

    /// <summary>
    /// после выполнения и перемещения файла - очистка очереди
    /// </summary>
    private void MarkAsProcessed(string filePath)
    {
        lock (_syncRoot)
        {
            _processedFiles.Remove(filePath);
        }
    }

    /// <summary>
    /// true, если файл обрабатывался
    /// </summary>
    private bool IsEnqueuedOrProcessed(string filePath)
    {
        lock (_syncRoot)
        {
            return _processedFiles.Contains(filePath);
        }
    }


    /// <summary>
    /// Асинхронно обрабатывает отдельный аудиофайл.
    /// </summary>
    /// <param name="filePath">Путь к обрабатываемому файлу.</param>
    /// <param name="outputPath">Путь к выходной директории.</param>
    /// <param name="state">Объект состояния процесса.</param>
    /// <param name="ct">Токен отмены.</param>
    private async Task ProcessAudioFileAsync(string filePath, string outputPath, string selectedModel, ProcessStateWpf state, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            UpdateConsole(state, $"Выполняется: {Path.GetFileName(filePath)}\n####################", true);

            var progress = new Progress<string>(message => UpdateConsole(state, message, true));

            var transcription = await _whisperService.TranscribeAsync(filePath, selectedModel, progress, ct);

            var txtFilePath = Path.ChangeExtension(filePath, ".txt");

            await File.WriteAllTextAsync(txtFilePath, transcription, ct);
            MoveProcessedFile(filePath, outputPath);

            UpdateConsole(state, $"Обработан: {Path.GetFileName(filePath)}", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки файла {FilePath}", filePath);
            UpdateConsole(state, $"Ошибка при обработке файла {Path.GetFileName(filePath)}: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Перемещает обработанный файл в выходную директорию.
    /// </summary>
    /// <param name="filePath">Путь к исходному файлу.</param>
    /// <param name="outputPath">Путь к выходной директории.</param>
    private void MoveProcessedFile(string filePath, string outputPath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            var fileName = Path.GetFileName(filePath);
            var destination = Path.Combine(outputPath, fileName);

            File.Move(filePath, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при перемещении файла {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Останавливает процесс обработки файлов.
    /// </summary>
    public void StopProcessing()
    {
        _cts?.Cancel();

        lock (_syncRoot)
        {
            _fileQueue.Clear();
            _processedFiles.Clear();
        }
    }

    /// <summary>
    /// Обновляет консольное сообщение о состоянии процесса.
    /// </summary>
    /// <param name="state">Объект состояния процесса.</param>
    /// <param name="message">Новое сообщение.</param>
    /// <param name="prepend">Флаг для добавления сообщения в начало (true) или в конец (false).</param>
    private void UpdateConsole(ProcessStateWpf state, string message, bool prepend)
    {
        var newMessage = prepend
            ? $"[{DateTime.Now:T}] {message}\n{state.ConsoleMessage}"
            : $"{state.ConsoleMessage}\n[{DateTime.Now:T}] {message}";

        state.ConsoleMessage = string.Join("\n", newMessage.Split('\n').Take(10));
    }
}
