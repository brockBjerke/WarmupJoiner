using Avalonia.Controls;

namespace WarmupJoiner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Keep the log scrolled to the newest entry as it grows.
        LogTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
            }
        };
    }
}
