using System.Windows.Controls;
using System.Windows;

namespace Shield.Estimator.Wpf;

public class PromptEditDialog : Window
{
    public string Prompt { get; private set; }

    public PromptEditDialog(string currentPrompt)
    {
        Title = "Edit Prompt";
        Width = 400;
        Height = 400;

        var textBox = new TextBox
        {
            Text = currentPrompt,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 75,
            Height = 25,
            Margin = new Thickness(5)
        };
        okButton.Click += (s, e) =>
        {
            Prompt = textBox.Text;
            DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 75,
            Height = 25,
            Margin = new Thickness(5)
        };
        cancelButton.Click += (s, e) => DialogResult = false;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var mainPanel = new DockPanel();
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        mainPanel.Children.Add(buttonPanel);
        mainPanel.Children.Add(textBox);

        Content = mainPanel;
    }
}
