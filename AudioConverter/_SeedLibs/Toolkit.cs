//Toolkit.cs
using System.Diagnostics;
using System.Text.Json;

namespace Shield.Estimator.AudioConverter._SeedLibs;

public class ConsoleCol
{
    private static readonly object consoleLock = new object();
    public static void WriteLine(string text, ConsoleColor color)
    {
        lock (consoleLock)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }
    public static void Write(string text, ConsoleColor color)
    {
        lock (consoleLock)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }
    }
}

public class Files
{
    public static void DeleteFilesByPath(params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }
    public static void CreateDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
    public static void DeleteDirectory(string directoryPath)
    {
        DirectoryInfo dir = new DirectoryInfo(directoryPath);
        if (dir.Exists)
        {
            dir.Delete(true);
        }
    }
}

public class IniFile
{
    public static async Task<string> ReadFile(string path)
    {
        using StreamReader reader = new StreamReader(path);
        return await reader.ReadToEndAsync();
    }
    public static async Task WriteFile(string path, string value)
    {
        // полная перезапись файла (false)
        using StreamWriter writer = new StreamWriter(path, false);
        await writer.WriteLineAsync(value);
    }
}
public class Cmd
{
    public static async Task RunProcess(string executablePath, string arguments)
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = executablePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(executablePath);

                process.Start();
                await process.WaitForExitAsync();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Decoder failed with exit code {process.ExitCode}:\n{error}");
                }

                Console.WriteLine("Cmd.RunProcess => Output: " + output);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running decoder: {ex.Message}");
            throw;
        }
    }

}

public class FileLogger
{
    private readonly string _filePath;
    private readonly int _maxLines;
    private readonly object _lockObj = new object();
    private bool _disposed;

    public FileLogger(string filePath, int maxLines = 800)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _maxLines = maxLines > 0 ? maxLines : throw new ArgumentException("Max lines must be greater than 0", nameof(maxLines));
        Files.CreateDirectory(Path.GetDirectoryName(_filePath));
        InitializeFile();
    }

    private void InitializeFile()
    {
        lock (_lockObj)
        {
            if (!File.Exists(_filePath))
            {
                using (File.Create(_filePath)) { }
            }
            else
            {
                TruncateFileIfNeeded(File.ReadAllLines(_filePath).ToList());
            }
        }
    }
    private void TruncateFileIfNeeded(List<string> lines)
    {
        if (lines.Count >= _maxLines)
        {
            int removeCount = lines.Count - _maxLines + 1;
            lines.RemoveRange(0, removeCount);
        }
    }

    public void Log(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";

        lock (_lockObj)
        {
            if (_disposed) return;

            try
            {
                var lines = File.ReadAllLines(_filePath).ToList();
                lines.Add(logEntry);
                TruncateFileIfNeeded(lines);
                File.WriteAllLines(_filePath, lines);
            }
            catch (Exception ex)
            {
                throw new IOException("Failed to write log entry", ex);
            }
        }
    }
    public void Dispose()
    {
        lock (_lockObj)
        {
            _disposed = true;
        }
    }
}

public static class SimpleJson<T> where T : class
{
    public static async Task<List<T>> LoadItemsAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<T>();
            }
            else
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                return JsonSerializer.Deserialize<List<T>>(json, options) ?? new List<T>();
            }
        }
        else
        {
            return new List<T>();
        }
    }
    public static async Task SaveItemsAsync(string filePath, List<T> items)
    {
        var json = JsonSerializer.Serialize(items);
        await File.WriteAllTextAsync(filePath, json);
    }
    public static async Task AddItemAsync(string filePath, List<T> items, T newItem)
    {
        if (newItem != null)
        {
            items.Add(newItem);
            await SaveItemsAsync(filePath, items);
        }
    }
    public static async Task DeleteItemAsync(string filePath, List<T> items, T item)
    {
        if (items.Contains(item))
        {
            items.Remove(item);
            await SaveItemsAsync(filePath, items);
        }
    }
    public static async Task UpdateItemAsync(string filePath, List<T> items, T item, Func<T, bool> predicate)
    {
        var existingItem = items.FirstOrDefault(predicate);
        if (existingItem != null)
        {
            var index = items.IndexOf(existingItem);
            items[index] = item;
            await SaveItemsAsync(filePath, items);
        }
    }
}
