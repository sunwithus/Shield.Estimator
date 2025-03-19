//FileProcessor.cs

using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Services.WhisperNet;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Shield.Estimator.Wpf;

public class FileProcessor
{
    private readonly WhisperNetService _whisperService;
    private readonly ILogger<FileProcessor> _logger;
    private FileSystemWatcher _fileWatcher;
    private CancellationTokenSource _cts;
    private const int MinutesTimeOut = 20;

    public FileProcessor(
        WhisperNetService whisperService,
        ILogger<FileProcessor> logger)
    {
        _whisperService = whisperService;
        _logger = logger;
    }

    /// <summary>
    /// Выполнение Whisper для папки с файлами wav, mp3.
    /// </summary>
    /// <param name="inputPath">Входная директория с аудиофайлами (сюда же сохранятся txt)</param>
    /// <param name="outputPath">Выходная директория с обработанными аудиофайлами</param>
    /// <param name="state">Состояния выполнения Singleton</param>

    public async Task ProcessExistingFilesAsync(string inputPath, string outputPath, ProcessStateWpf state)
    {
        _cts = new CancellationTokenSource();

        while (!_cts.Token.IsCancellationRequested)
        {
            var files = Directory.GetFiles(inputPath, "*.*").Where(s => s.EndsWith(".mp3") || s.EndsWith(".wav"));
            foreach (var file in files)
            {
                if (_cts.Token.IsCancellationRequested) break;
                await ProcessAudioFileAsync(file, outputPath, state, _cts.Token);
            }
            await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
        }
    }
    private async Task ProcessAudioFileAsync(string filePath, string outputPath, ProcessStateWpf state, CancellationToken ct)
    {
        try
        {
            UpdateConsole(state, $"Выполняется: {Path.GetFileName(filePath)}\n#####", true);
            var transcription = await _whisperService.TranscribeAsync(filePath);

            var txtFilePath = Path.ChangeExtension(filePath, ".txt");

            await File.WriteAllTextAsync(txtFilePath, transcription, ct);
            MoveProcessedFile(filePath, outputPath);

            UpdateConsole(state, $"Обработан: {Path.GetFileName(filePath)}", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки файла {FilePath}", filePath);
            UpdateConsole(state, $"Ошибка в файле {Path.GetFileName(filePath)}: {ex.Message}", true);
        }
    }

    private void MoveProcessedFile(string filePath, string outputPath)
    {
        string fileName = Path.GetFileName(filePath);
        /*
        if (Path.GetExtension(filePath).ToLower() != ".txt") 
        {
            fileName = $"{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}_{Path.GetFileName(filePath)}";
        }
        */
        var destination = Path.Combine(outputPath, fileName);
        File.Move(filePath, destination, overwrite: true);
    }

    public void StopProcessing()
    {
        _cts?.Cancel();
        _fileWatcher?.Dispose();
    }

    private void UpdateConsole(ProcessStateWpf state, string message, bool prepend)
    {
        var newMessage = prepend
            ? $"[{DateTime.Now:T}] {message}\n{state.ConsoleMessage}"
            : $"{state.ConsoleMessage}\n[{DateTime.Now:T}] {message}";

        state.ConsoleMessage = string.Join("\n", newMessage.Split('\n').Take(10));
    }

    ////////////////////////////////////////////////////////////////////////////////////
    /// Инициялизация для постоянного мониторинга директории (сейчас не исипользуется)
    ////////////////////////////////////////////////////////////////////////////////////
    public void InitializeFileMonitoring(string inputPath, string outputPath)
    {
        _fileWatcher = new FileSystemWatcher(inputPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        _fileWatcher.Created += async (sender, e) =>
        {
            await ProcessNewFileAsync(e.FullPath, outputPath);
        };
    }
    private async Task ProcessNewFileAsync(string filePath, string outputPath)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(MinutesTimeOut));
            await WaitForFileReady(filePath, timeoutCts.Token);

            var transcription = await _whisperService.TranscribeAsync(filePath);
            await SaveTranscriptionResult(filePath, outputPath, transcription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FileName}", Path.GetFileName(filePath));
        }
    }

    private async Task WaitForFileReady(string filePath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(1000, ct);
            }
        }
        throw new OperationCanceledException("File access timeout");
    }

    private async Task SaveTranscriptionResult(string sourcePath, string outputDir, string transcription)
    {
        var outputFileName = $"{Path.GetFileName(sourcePath)}.txt";
        var outputPath = Path.Combine(outputDir, outputFileName);

        await File.WriteAllTextAsync(outputPath, transcription);
        ArchiveSourceFile(sourcePath, outputDir);
    }

    private void ArchiveSourceFile(string sourcePath, string archiveDir)
    {
        var archivePath = Path.Combine(archiveDir, Path.GetFileName(sourcePath));

        if (File.Exists(archivePath))
            archivePath = Path.Combine(archiveDir,
                $"{Path.GetFileNameWithoutExtension(sourcePath)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(sourcePath)}");

        File.Move(sourcePath, archivePath);
    }
}
