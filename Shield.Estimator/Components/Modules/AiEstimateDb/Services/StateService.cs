using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services
{
    public class StateService
    {
        public DateTime StartDate { get; set; } = DateTime.Now.AddMonths(-1);
        public DateTime EndDate { get; set; } = DateTime.Now.AddMonths(1).AddYears(1);
        public int TimeInterval { get; set; } = 10;
        public bool IsPlayingNow { get; set; } = false;
        public bool IsStoped { get; set; } = true;
        public bool IsStopPressed { get; set; } = true;
        public bool IsCycle { get; set; } = true;
        public int CycleInterval { get; set; } = 3;
        public int ProcessedKeys { get; set; } = 0;
        public int ProcessedAi { get; set; } = 0;
        public int TotalKeys { get; set; } = 0;
        public string ProcessingMessage { get; set; } = "";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ChangeState(bool isPlayingNow, bool isStoped, string processingMessage, int processedKeys = -1, int totalKeys = -1, int processedAi = 0)
        {
            if (processedKeys >= 0 && totalKeys >= 0)
            {
                ProcessedKeys = processedKeys;
                ProcessedAi = processedAi;
                TotalKeys = totalKeys;
            }
            ProcessingMessage = processingMessage;
            IsStoped = isStoped;
            IsPlayingNow = isPlayingNow;
            OnPropertyChanged();
        }
    }

    public class StateService2
    {
        public DateTime StartDate2 { get; set; } = DateTime.Now.AddMonths(-1);
        public DateTime EndDate2 { get; set; } = DateTime.Now.AddMonths(1).AddYears(1);
        public string TimeInterval2 { get; set; } = "00:00:10";
        public bool IsPlayingNow2 { get; set; } = false;
        public bool IsStoped2 { get; set; } = true;
        public bool IsStopPressed2 { get; set; } = true;
        public bool IsCycle2 { get; set; } = false;
        public int CycleInterval2 { get; set; } = 2;
        public int ProcessedKeys2 { get; set; } = 0;
        public int TotalKeys2 { get; set; } = 0;
        public string ProcessingMessage2 { get; set; } = "";

        public event PropertyChangedEventHandler PropertyChanged2;

        protected virtual void OnPropertyChanged2([CallerMemberName] string propertyName = null)
        {
            PropertyChanged2?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ChangeState2(bool isPlayingNow, bool isStoped, string processingMessage, int processedKeys = -1, int totalKeys = -1)
        {
            if (processedKeys >= 0 && totalKeys >= 0)
            {
                ProcessedKeys2 = processedKeys;
                TotalKeys2 = totalKeys;
            }
            ProcessingMessage2 = processingMessage;
            IsStoped2 = isStoped;
            IsPlayingNow2 = isPlayingNow;
            OnPropertyChanged2();
        }
    }

}