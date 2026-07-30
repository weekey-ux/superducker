using System.Windows;
using System.Windows.Input;

namespace SuperDucker.App;

public partial class CloseChoiceDialog : Window
{
    /// <summary>
    /// Whether the user checked "记住选择".
    /// </summary>
    public bool RememberChoice => CbRemember.IsChecked == true;

    public CloseChoiceDialog()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true; // true = minimize to tray
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false; // false = exit
    }

    private void CloseDialog_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = null; // null = cancelled (X button)
    }
}
