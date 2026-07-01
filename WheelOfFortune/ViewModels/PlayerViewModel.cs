using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FortuneWheel.Models;

namespace FortuneWheel.ViewModels;

/// <summary>VM-обёртка над Player для привязки к UI.</summary>
public sealed class PlayerViewModel : INotifyPropertyChanged
{
    public Player Model { get; }

    public int Id => Model.Id;
    public string Name => Model.Name;
    public bool IsActive => Model.IsActive;
    public string StatusText => IsActive ? "" : " (скрыт)";
    public Brush NameBrush => IsActive ? Brushes.Black : Brushes.Gray;

    private int _coefficient;
    public int Coefficient
    {
        get => _coefficient;
        set { if (_coefficient != value) { _coefficient = value; OnPropertyChanged(); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private bool _isHighlighted;
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set { if (_isHighlighted != value) { _isHighlighted = value; OnPropertyChanged(); } }
    }

    // --- Статистика ---
    private int _participations;
    public int Participations
    {
        get => _participations;
        private set
        {
            if (_participations != value)
            {
                _participations = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WinRate));
                OnPropertyChanged(nameof(WinRateText));
            }
        }
    }

    private int _wins;
    public int Wins
    {
        get => _wins;
        private set
        {
            if (_wins != value)
            {
                _wins = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WinRate));
                OnPropertyChanged(nameof(WinRateText));
            }
        }
    }

    private double? _avgWinCoefficient;
    public double? AvgWinCoefficient
    {
        get => _avgWinCoefficient;
        private set
        {
            if (_avgWinCoefficient != value)
            {
                _avgWinCoefficient = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvgWinCoefText));
            }
        }
    }

    /// <summary>Процент побед (0–100).</summary>
    public double WinRate => Participations == 0 ? 0 : (double)Wins / Participations * 100;

    /// <summary>Текстовое представление процента побед.</summary>
    public string WinRateText => Participations == 0 ? "—" : $"{WinRate:F1}%";

    /// <summary>Текстовое представление среднего коэффициента при победе.</summary>
    public string AvgWinCoefText => AvgWinCoefficient.HasValue ? $"{AvgWinCoefficient.Value:F2}" : "—";

    public PlayerViewModel(Player model)
    {
        Model = model;
        _coefficient = model.Coefficient;
        _participations = model.Participations;
        _wins = model.Wins;
        _avgWinCoefficient = model.AvgWinCoefficient;
    }

    /// <summary>Подтягивает все данные из модели после изменений.</summary>
    public void RefreshFromModel()
    {
        Coefficient = Model.Coefficient;
        Participations = Model.Participations;
        Wins = Model.Wins;
        AvgWinCoefficient = Model.AvgWinCoefficient;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}