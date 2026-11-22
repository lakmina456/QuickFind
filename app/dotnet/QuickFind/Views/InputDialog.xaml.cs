using System.Windows;

namespace QuickFind.Views;

public partial class InputDialog : Window
{
    public string ResultText { get; private set; } = "";

    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public static string? Prompt(Window owner, string title, string prompt, string initial)
    {
        var dlg = new InputDialog(title, prompt, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.ResultText : null;
    }
}
