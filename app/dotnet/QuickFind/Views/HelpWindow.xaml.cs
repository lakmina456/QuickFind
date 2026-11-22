using System.Windows;
using QuickFind.Core;

namespace QuickFind.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        HelpBox.Text = HelpText.Syntax;
    }
}
