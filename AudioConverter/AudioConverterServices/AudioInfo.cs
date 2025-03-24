using FFMpegCore;

namespace Shield.AudioConverter.AudioConverterServices;

public static class AudioInfo
{
    private static readonly SemaphoreSlim _semaphoreFfprobe = new SemaphoreSlim(1, 1);
    public static async Task<(int Duration, int Channels)> AnalyzeAudioAsync(string inputFileName)
    {
        await _semaphoreFfprobe.WaitAsync();
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(inputFileName);
            return (
                (int)(mediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0),
                mediaInfo.PrimaryAudioStream?.Channels ?? 0
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return (0, 1);
        }
        finally
        {
            _semaphoreFfprobe.Release();
        }
    }
}
