namespace Shield.AudioConverter.AudioConverterServices;

public interface IAudioConverter
{
    Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName);
    Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName);
    Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null);
    Task ConvertByteArrayToFileAsync(byte[] audioDataLeft, byte[] audioDataRight, string audioFilePath, string recordType = "", string eventCode = "");
}

