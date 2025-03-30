//FileProcessor.cs

using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Services.WhisperNet;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Shield.Estimator.Wpf;

/// <summary>
/// Класс для обработки аудиофайлов и их транскрибирования с помощью WhisperNet.
/// </summary>
public class FileProcessor
{
    private readonly WhisperNetService _whisperService;
    private readonly ILogger<FileProcessor> _logger;
    private FileSystemWatcher _fileWatcher;
    private CancellationTokenSource _cts;
    private const int MinutesTimeOut = 60;

    /// <summary>
    /// Конструктор класса FileProcessor.
    /// </summary>
    /// <param name="whisperService">Сервис для транскрибирования аудио.</param>
    /// <param name="logger">Логгер для записи ошибок и информации.</param>
    public FileProcessor(
        WhisperNetService whisperService,
        ILogger<FileProcessor> logger)
    {
        _whisperService = whisperService;
        _logger = logger;
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

        var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Аудио форматы
            ".mp3", ".wav", ".ogg", ".aac", ".flac", ".m4a", ".wma", ".aiff", ".alac",
            // Видео форматы
            ".mp4", ".avi", ".mov", ".mkv", ".flv", ".wmv", ".webm", ".mpeg", ".3gp",
            ".m4v", ".vob", ".ogg", ".mts", ".m2ts", ".ts"
        };

        while (!_cts.Token.IsCancellationRequested)
        {
            var files = Directory.GetFiles(inputPath, "*.*")
                .Where(file => mediaExtensions.Contains(Path.GetExtension(file))).ToList();
            
            foreach (var file in files)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await ProcessAudioFileAsync(file, outputPath, selectedModel, state, _cts.Token);
            }
            UpdateConsole(state, $"Ожидание новых файлов...", true);
            await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token); // Уменьшено время задержки
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

            UpdateConsole(state, $"Выполняется: {Path.GetFileName(filePath)}\n#####", true);

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

            // Если файл существует в выходной директории, добавляем временную метку
            /*
            if (File.Exists(destination))
            {
                fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{fileName}";
                destination = Path.Combine(outputPath, fileName);
            }
            */
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
        _fileWatcher?.Dispose();
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
