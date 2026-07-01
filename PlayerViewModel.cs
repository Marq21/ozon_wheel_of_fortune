using System.ComponentModel;
using System.Runtime.CompilerServices;
using FortuneWheel.Models;

namespace FortuneWheel.ViewModels;

/// <summary>VM-обёртка над Player для привязки к UI.</summary>
public sealed class PlayerViewModel : INotifyPropertyChanged
{
    public Player Model { get; }

    public int Id => Model.Id;
    public string Name => Model.Name;

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

    public PlayerViewModel(Player model)
    {
        Model = model;
        _coefficient = model.Coefficient;
    }

    /// <summary>Подтягивает коэффициент из модели после розыгрыша.</summary>
    public void RefreshFromModel() => Coefficient = Model.Coefficient;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
