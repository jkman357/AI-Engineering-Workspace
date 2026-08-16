using System.Windows;
using System.Windows.Controls;

namespace AIEngineeringWorkspace.Dialogs;

internal sealed class TextPromptDialog : Window
{
    private readonly TextBox _textBox;
    public string Value => _textBox.Text.Trim();

    public TextPromptDialog(Window? owner, string title, string prompt, string initialValue = "")
    {
        Owner = owner;
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var promptText = new TextBlock { Text = prompt, Margin = new Thickness(0,0,0,8), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(promptText, 0); root.Children.Add(promptText);
        _textBox = new TextBox { Text = initialValue, MinWidth = 380, Margin = new Thickness(0,0,0,12) };
        _textBox.SelectAll();
        _textBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { DialogResult = true; e.Handled = true; } };
        Grid.SetRow(_textBox, 1); root.Children.Add(_textBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okButton = new Button { Content = "OK", Width = 86, Margin = new Thickness(0,0,8,0), IsDefault = true };
        okButton.Click += (_, _) => DialogResult = true;
        var cancelButton = new Button { Content = "Cancel", Width = 86, IsCancel = true };
        buttons.Children.Add(okButton); buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _textBox.Focus();
    }
}
