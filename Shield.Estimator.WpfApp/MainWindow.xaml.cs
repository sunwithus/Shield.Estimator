//MainWindow.xaml.cs

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Win32;
using Shield.Estimator.Business.Services;
using System.IO;
using System.Windows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shield.Estimator.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ProcessStateWpf _processStateWpf;
        private readonly FileProcessor _fileProcessor;
        private readonly IConfigurationRoot _configuration;

        private CancellationTokenSource _cts;

        public MainWindow(ProcessStateWpf processStateWpf, FileProcessor fileProcessor, IConfiguration configuration)
        {
            InitializeComponent();
            _processStateWpf = processStateWpf;
            _fileProcessor = fileProcessor;
            DataContext = _processStateWpf;
            _configuration = (IConfigurationRoot)configuration;
        }
        
        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select a folder" };
            if (dialog.ShowDialog() == true)
            {
                if (sender == SelectInputButton) InputPathTextBox.Text = dialog.FolderName;
                else if (sender == SelectOutputButton) OutputPathTextBox.Text = dialog.FolderName;
            }
        }

        private async void StartProcessing_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            //_fileProcessor.InitializeFileMonitoring(InputPathTextBox.Text, OutputPathTextBox.Text);

            try
            {
                await _fileProcessor.ProcessExistingFilesAsync(
                    InputPathTextBox.Text,
                    OutputPathTextBox.Text,
                    _processStateWpf
                );
            }
            catch (OperationCanceledException)
            {
                _processStateWpf.ConsoleMessage = "Processing stopped";
            }
        }

        private void StopProcessing_Click(object sender, RoutedEventArgs e)
        {
            _fileProcessor.StopProcessing();

        }        

        private void EditPrompt_Click(object sender, RoutedEventArgs e)
        {
            var currentPrompt = _configuration["Prompt"] ?? string.Empty;
            var promptDialog = new PromptEditDialog(currentPrompt);
            if (promptDialog.ShowDialog() == true)
            {
                UpdateConfiguration("Prompt", promptDialog.Prompt);
            }
        }

        public void UpdateConfiguration(string key, string value)
        {
            _configuration[key] = value;

            // Получаем провайдер конфигурации JSON
            var jsonConfigProvider = _configuration.Providers.FirstOrDefault(p => p is JsonConfigurationProvider);

            if (jsonConfigProvider != null)
            {
                var jsonProvider = (JsonConfigurationProvider)jsonConfigProvider;
                var filePath = ((FileConfigurationSource)jsonProvider.Source).Path;

                // Читаем текущий JSON-конфиг
                var jsonConfig = File.ReadAllText(filePath);
                var jsonObject = JsonNode.Parse(jsonConfig);

                // Обновляем значение
                if (jsonObject is JsonObject rootObject)
                {
                    rootObject[key] = value;
                }
                else
                {
                    throw new InvalidOperationException("Invalid JSON structure");
                }

                // Сериализуем обратно с форматированием
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(filePath, jsonObject.ToJsonString(options));

                // Перезагружаем конфигурацию
                _configuration.Reload();
            }
        }


        private bool ValidatePaths()
        {
            if (string.IsNullOrWhiteSpace(InputPathTextBox.Text) ||
                string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                MessageBox.Show("Please select both input and output folders!");
                return false;
            }
            return true;
        }
        /*
        private void ToggleFileType_Click(object sender, RoutedEventArgs e)
        {
            bool currentValue = _configuration.GetValue<bool>("ProcessTextFiles");
            UpdateConfiguration("ProcessTextFiles", (!currentValue).ToString());
            UpdateFileTypeUI();
        }
        */

        /*
        private void UpdateFileTypeUI()
        {
            bool processTextFiles = _configuration.GetValue<bool>("ProcessTextFiles");
            ToggleFileTypeButton.Content = processTextFiles ? "Switch to Excel Files" : "Switch to Text Files";
            FileTypeStatusTextBlock.Text = $"Current mode: {(processTextFiles ? ".TXT" : ".XLSX")}";
        }
        */
    }
}