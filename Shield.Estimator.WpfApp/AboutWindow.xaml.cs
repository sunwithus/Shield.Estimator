using System.Reflection;
using System.Windows;

namespace Shield.Estimator.Wpf
{
    public partial class AboutWindow : Window
    {
        //public string Version => GetAssemblyVersion();

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
        }
        /*
        private string GetAssemblyVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }
        */
        // Добавленный обработчик
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close(); // Или this.Close();
        }
    }
}
