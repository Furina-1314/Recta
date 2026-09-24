using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Recta.App.Windows;

/// <summary>单值输入对话框(改名等轻量场景)。</summary>
public partial class TextInputDialog : Window
{
    public string? Value { get; private set; }

    public TextInputDialog()
    {
        InitializeComponent();
    }

    public TextInputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Value = null;
        Close();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text;
        Close();
    }
}
