namespace Shield.AudioConverter.Options;

public class AudioConverterOptions
{
    public int TargetSampleRate { get; set; } = 16000;
    public int TargetBitRate { get; set; } = 256000;
    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "tempFFMpeg");
}
