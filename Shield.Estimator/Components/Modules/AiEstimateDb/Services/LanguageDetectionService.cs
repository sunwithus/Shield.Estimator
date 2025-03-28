using Shield.Estimator.Business.Services;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services
{
    public class LanguageDetectionService
    {
        //private readonly AsyncRetryPolicy _retryPolicy;
        private readonly WhisperFasterDockerService _whisperFaster;
        private readonly ILogger<LanguageDetectionService> _logger;
        private const double MinimumConfidence = 0.65;

        public LanguageDetectionService(
            WhisperFasterDockerService whisperFaster,
            ILogger<LanguageDetectionService> logger)
        {
            _logger = logger;
            _whisperFaster = whisperFaster;
            //_retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(5, retryAttempt =>
            //    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        internal async Task<(string languageCode, string detectedLanguage, double confidence)> DetectLanguageAsync(string audioFilePath)
        {
            string languageCode = "none";
            string detectedLanguage = "undefined";
            double confidence = 0;
            try
            {
                /*
                await _retryPolicy.ExecuteAsync(async () =>
                {
                */
                    (languageCode, detectedLanguage, confidence) = await _whisperFaster.DetectLanguageAsync(audioFilePath);
                    detectedLanguage = detectedLanguage + " " + Math.Round(confidence * 100, 1, MidpointRounding.AwayFromZero).ToString("N1") + "%";

                    if (confidence < MinimumConfidence)
                    {
                        languageCode = "none";
                        detectedLanguage = "undefined";
                    }
                /*
                });
                */
                _logger.LogInformation($"{languageCode}, {detectedLanguage}, {confidence}");
                return (languageCode, detectedLanguage, confidence);
            }
            catch (Exception ex)
            {
                detectedLanguage = "error";
                _logger.Log(LogLevel.Error, "General Error AiBGService DetectLanguageAsync => " + ex.Message);
                return (languageCode, detectedLanguage, confidence);
            }
        }
    }
}
