using System.Windows;

namespace FortuneWheel.Views;

/// <summary>Окно ввода секретного кода.</summary>
public partial class SecretCodeWindow : Window
{
    /// <summary>Введённый код (после закрытия окна по OK).</summary>
    public string? EnteredCode { get; private set; }

    public SecretCodeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            CodeInput.Focus();
            CodeInput.SelectAll();
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var code = CodeInput.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            ErrorText.Text = "Введите код.";
            return;
        }
        EnteredCode = code;
        DialogResult = true;
        Close();
    }
}
