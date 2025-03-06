namespace Shield.Estimator.Business.AudioConverterServices;

public interface IAudioConverter
{
    Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName);
    Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null);
}

