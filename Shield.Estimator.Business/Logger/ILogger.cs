namespace Shield.Estimator.Business.Logger;

public interface ILogger
{
    /// <summary>
    /// Добавление информационных сообщений в файл лога приложения
    /// </summary>
    /// <param name="message"></param>
    Task AddLogMessage(string message);
}
