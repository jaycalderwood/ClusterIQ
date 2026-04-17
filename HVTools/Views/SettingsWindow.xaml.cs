using System.Windows;

namespace HVTools.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(object dataContext)
    {
        InitializeComponent();
        DataContext = dataContext;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }


private void Save_Click(object sender, RoutedEventArgs e)
{
    DialogResult = true;
    Close();
}

private void Cancel_Click(object sender, RoutedEventArgs e)
{
    DialogResult = false;
    Close();
}

}
