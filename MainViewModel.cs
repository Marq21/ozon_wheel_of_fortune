using FortuneWheel.Models;
using FortuneWheel.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FortuneWheel.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DbService _db;
    private readonly WheelService _wheel = new();

    public ObservableCollection<PlayerViewModel> Players { get; } = new();

    private PlayerViewModel? _selectedPlayer;
    public PlayerViewModel? SelectedPlayer
    {
        get => _selectedPlayer;
        set { if (_selectedPlayer != value) { _selectedPlayer = value; OnPropertyChanged(); } }
    }

    private string _newPlayerName = string.Empty;
    public string NewPlayerName
    {
        get => _newPlayerName;
        set
        {
            if (_newPlayerName != value)
            {
                _newPlayerName = value;
                OnPropertyChanged();
            }
        }
    }

    private string _resultText = "Добавьте сотрудников и нажмите «Крутить!»";
    public string ResultText
    {
        get => _resultText;
        set { if (_resultText != value) { _resultText = value; OnPropertyChanged(); } }
    }

    private bool _isSpinning;
    public bool IsSpinning
    {
        get => _isSpinning;
        set { if (_isSpinning != value) { _isSpinning = value; OnPropertyChanged(); } }
    }

    public ICommand AddCommand { get; }
    public ICommand SoftDeleteCommand { get; }
    public ICommand HardDeleteCommand { get; }
    public ICommand SpinCommand { get; }

    public MainViewModel(DbService db)
    {
        _db = db;

        AddCommand = new RelayCommand(
            _ => AddPlayer(),
            _ => !IsSpinning && !string.IsNullOrWhiteSpace(NewPlayerName));

        SoftDeleteCommand = new RelayCommand(
            _ => SoftDelete(),
            _ => !IsSpinning && SelectedPlayer != null && ActiveCount() > 1);

        HardDeleteCommand = new RelayCommand(
            _ => HardDelete(),
            _ => !IsSpinning && SelectedPlayer != null);

        SpinCommand = new RelayCommand(
            _ => _ = SpinAsync(),
            _ => !IsSpinning && Players.Any(p => p.IsSelected));

        LoadPlayers();
    }

    private int ActiveCount() => Players.Count;

    private void LoadPlayers()
    {
        var all = _db.LoadActive();
        Players.Clear();
        foreach (var p in all) Players.Add(new PlayerViewModel(p));
    }

    private void AddPlayer()
    {
        var name = NewPlayerName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var player = _db.Add(name);
        if (player is null)
        {
            ResultText = "⚠ Сотрудник с таким именем уже существует.";
            return;
        }
        Players.Add(new PlayerViewModel(player));
        NewPlayerName = string.Empty;
        ResultText = $"✅ Добавлен: {player.Name}";
    }

    private void SoftDelete()
    {
        if (SelectedPlayer is null) return;
        if (ActiveCount() <= 1)
        {
            ResultText = "⚠ Нельзя скрыть последнего активного сотрудника.";
            return;
        }
        var name = SelectedPlayer.Name;
        _db.SoftDelete(SelectedPlayer.Id);
        Players.Remove(SelectedPlayer);
        SelectedPlayer = null;
        ResultText = $"🙈 {name} скрыт из списка (история сохранена).";
    }

    private void HardDelete()
    {
        if (SelectedPlayer is null) return;
        var name = SelectedPlayer.Name;
        var res = MessageBox.Show(
            $"Полностью стереть сотрудника «{name}»?\nЭто действие необратимо.",
            "Подтверждение жёсткого удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        _db.HardDelete(SelectedPlayer.Id);
        Players.Remove(SelectedPlayer);
        SelectedPlayer = null;
        ResultText = $"🗑 {name} полностью удалён.";
    }

    private async Task SpinAsync()
    {
        var participantIds = Players.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        if (participantIds.Count == 0)
        {
            ResultText = "⚠ Отметьте хотя бы одного участника смены.";
            return;
        }

        IsSpinning = true;
        ResultText = "🎰 Крутим колесо...";

        var participants = Players.Where(p => p.IsSelected).ToList();
        var allModels = Players.Select(p => p.Model).ToList();

        // Запускаем анимацию параллельно с вычислением результата
        var animTask = AnimateWheelAsync(participants);
        var winner = _wheel.Spin(allModels, participantIds);
        await animTask;

        if (winner is null)
        {
            ResultText = "⚠ Не удалось определить победителя.";
            IsSpinning = false;
            return;
        }

        // Сохраняем обновлённые коэффициенты в БД
        _db.UpdateCoefficients(allModels);

        // Обновляем UI
        foreach (var vm in Players)
        {
            vm.IsHighlighted = false;
            vm.RefreshFromModel();
        }

        var winnerVm = Players.FirstOrDefault(p => p.Id == winner.Id);
        if (winnerVm is not null) winnerVm.IsHighlighted = true;

        ResultText = $"🎉 Закрывает смену: {winner.Name}!";
        IsSpinning = false;
    }

    /// <summary>Анимация «бегущей подсветки» с постепенным замедлением.</summary>
    private async Task AnimateWheelAsync(List<PlayerViewModel> participants)
    {
        var rng = new Random();
        int totalSteps = 28 + rng.Next(10);

        try
        {
            for (int i = 0; i < totalSteps; i++)
            {
                var target = participants[rng.Next(participants.Count)];
                foreach (var p in participants) p.IsHighlighted = false;
                target.IsHighlighted = true;

                // Квадратичное замедление: от ~60 мс до ~600 мс
                int delay = 60 + i * i * 4;
                await Task.Delay(Math.Min(delay, 600));
            }
        }
        finally
        {
            foreach (var p in participants) p.IsHighlighted = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
