using System.Windows;

namespace FortuneWheel.Views;

/// <summary>Окно ввода секретного кода.</summary>
public partial class SecretCodeWindow : Window
{
    /// <summary>Введённый код (после закрытия окна по OK).</summary>
    public string? EnteredCode { get; private set; }

    /// <summary>Событие: пользователь нажал "Применить" (выполнить без закрытия).</summary>
    public event System.EventHandler<string>? ApplyRequested;

    public SecretCodeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            CodeInput.Focus();
            CodeInput.SelectAll();
        };
    }

    /// <summary>OK — выполнить код и закрыть окно.</summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var code = GetValidatedCode();
        if (code is null) return;
        EnteredCode = code;
        DialogResult = true;
        Close();
    }

    /// <summary>Применить — выполнить код, но НЕ закрывать окно.</summary>
    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var code = GetValidatedCode();
        if (code is null) return;
        ApplyRequested?.Invoke(this, code);
        CodeInput.Clear();
        CodeInput.Focus();
    }

    /// <summary>Валидация ввода. Возвращает код или null, если пусто.</summary>
    private string? GetValidatedCode()
    {
        var code = CodeInput.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            ErrorText.Text = "Введите код.";
            return null;
        }
        ErrorText.Text = string.Empty;
        return code;
    }
}