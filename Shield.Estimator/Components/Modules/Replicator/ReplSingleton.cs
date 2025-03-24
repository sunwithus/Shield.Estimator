//ReplSingleton.cs
//не используется
using System.ComponentModel;

namespace Shield.Estimator.Shared.Components.Modules.Replicator
{
    public class ReplSingleton
    {
        private static ReplSingleton instance;
        private static readonly object padlock = new object();

        private ReplSingleton()
        {
        }

        public static ReplSingleton ReplInstance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new ReplSingleton();
                    }
                    return instance;
                }
            }
        }

        private bool isStoped = true;
        public bool IsStoped
        {
            get { return isStoped; }
            set
            {
                isStoped = value;
                OnPropertyChanged(nameof(IsStoped));
            }
        }

        private int progressExec = 0;
        public int ProgressExec
        {
            get { return progressExec; }
            set
            {
                progressExec = value;
                OnPropertyChanged(nameof(ProgressExec));
            }
        }

        public event PropertyChangedEventHandler ReplPropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            ReplPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
