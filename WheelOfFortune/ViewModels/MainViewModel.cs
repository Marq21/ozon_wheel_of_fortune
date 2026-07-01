using FortuneWheel.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    private readonly SoundService _sound = new();
    private readonly string _dbPath;
    private readonly App.StorageMode _storageMode;

    private bool _showAllPlayers;
    public bool ShowAllPlayers
    {
        get => _showAllPlayers;
        set
        {
            if (_showAllPlayers != value)
            {
                _showAllPlayers = value;
                OnPropertyChanged();
                LoadPlayers();
            }
        }
    }

    /// <summary>Индекс выбранной вкладки (0 = Розыгрыш, 1 = Статистика).</summary>
    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged();
                if (value == 1) LoadStats();
            }
        }
    }

    public ObservableCollection<PlayerViewModel> Players { get; } = new();
    public ObservableCollection<PlayerViewModel> StatsPlayers { get; } = new();

    private PlayerViewModel? _selectedPlayer;
    public PlayerViewModel? SelectedPlayer
    {
        get => _selectedPlayer;
        set { if (_selectedPlayer != value) { _selectedPlayer = value; OnPropertyChanged(); } }
    }

    public string DatabaseFolder => Path.GetDirectoryName(_dbPath) ?? string.Empty;
    public string StorageModeText => _storageMode == App.StorageMode.Portable
        ? "🔌 Portable (данные рядом с exe)"
        : "💾 Обычный режим (данные в %AppData%)";


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
    public ICommand RestoreCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand OpenDbFolderCommand { get; }
    public ICommand SecretCodeCommand { get; }

    public MainViewModel(DbService db, string dbPath, App.StorageMode storageMode)
    {
        _db = db;
        _dbPath = dbPath;
        _storageMode = storageMode;

        OpenDbFolderCommand = new RelayCommand(
            _ => System.Diagnostics.Process.Start("explorer.exe", DatabaseFolder),
            _ => Directory.Exists(DatabaseFolder));

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

        RestoreCommand = new RelayCommand(
            param => RestorePlayer(param as PlayerViewModel),
            _ => !IsSpinning);

        ExportCommand = new RelayCommand(_ => ExportDatabase(), _ => !IsSpinning);
        ImportCommand = new RelayCommand(_ => ImportDatabase(), _ => !IsSpinning);
        SecretCodeCommand = new RelayCommand(
            _ => ShowSecretCodeDialog(),
            _ => !IsSpinning);

        LoadPlayers();
    }

    private int ActiveCount() => Players.Count;

    private void LoadPlayers()
    {
        var all = ShowAllPlayers ? _db.LoadAll() : _db.LoadActive();
        Players.Clear();
        foreach (var p in all) Players.Add(new PlayerViewModel(p));
    }

    /// <summary>Загружает полную статистику по всем сотрудникам (включая скрытых).</summary>
    private void LoadStats()
    {
        var all = _db.LoadAll();
        StatsPlayers.Clear();
        foreach (var p in all) StatsPlayers.Add(new PlayerViewModel(p));
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

    private void RestorePlayer(PlayerViewModel? player)
    {
        if (player is null || player.IsActive) return;
        var name = player.Name;
        _db.Restore(player.Id);
        LoadPlayers();
        ResultText = $"✅ {name} восстановлен в списке.";
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

        _sound.PlaySpinStart();

        IsSpinning = true;
        ResultText = "🎰 Крутим колесо...";

        var participants = Players.Where(p => p.IsSelected).ToList();
        var allModels = Players.Select(p => p.Model).ToList();

        var animTask = AnimateWheelAsync(participants);
        var (winner, coefAtWin) = _wheel.Spin(allModels, participantIds);
        await animTask;

        if (winner is null)
        {
            ResultText = "⚠ Не удалось определить победителя.";
            IsSpinning = false;
            return;
        }

        _sound.PlayWin();

        // Сохраняем коэффициенты и записываем в историю
        _db.UpdateCoefficients(allModels);
        _db.RecordSpin(winner.Id, coefAtWin, participantIds.Count, participantIds);

        // Обновляем UI — подтягиваем новые значения из моделей
        foreach (var vm in Players)
        {
            vm.IsHighlighted = false;
            if (participantIds.Contains(vm.Id))
            {
                vm.Model.Participations++;
                if (vm.Id == winner.Id)
                {
                    vm.Model.Wins++;
                    vm.Model.AvgWinCoefficient = ComputeAvgCoef(vm.Id);
                }
            }
            vm.RefreshFromModel();
        }

        var winnerVm = Players.FirstOrDefault(p => p.Id == winner.Id);
        if (winnerVm is not null) winnerVm.IsHighlighted = true;

        ResultText = $"🎉 Сегодня счастливчиком оказался: {winner.Name}!\n" +
                     $"Коэффициент при победе: {coefAtWin} | Участников: {participantIds.Count}";
        IsSpinning = false;
    }

    /// <summary>Вычисляет средний коэффициент побед для конкретного игрока.</summary>
    private double? ComputeAvgCoef(int playerId)
    {
        // Используем кэшированное значение из модели — оно уже актуально после RecordSpin
        var vm = Players.FirstOrDefault(p => p.Id == playerId);
        return vm?.Model.AvgWinCoefficient;
    }

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

                _sound.PlayTick();

                int delay = 60 + i * i * 4;
                await Task.Delay(Math.Min(delay, 600));
            }
        }
        finally
        {
            foreach (var p in participants) p.IsHighlighted = false;
        }
    }

    private void ExportDatabase()
    {
        try
        {
            var csv = _db.ExportToCsv();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"fortune_backup_{timestamp}.csv";

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv",
                FileName = fileName,
                Title = "Экспорт базы сотрудников"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveDialog.FileName, csv, System.Text.Encoding.UTF8);
                ResultText = $"💾 База экспортирована: {saveDialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            ResultText = $"⚠ Ошибка экспорта: {ex.Message}";
        }
    }

    private void ImportDatabase()
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV файлы (*.csv)|*.csv",
            Title = "Выберите файл резервной копии"
        };

        if (openDialog.ShowDialog() != true) return;

        var res = MessageBox.Show(
            "Импорт добавит или заменит сотрудников из выбранного файла.\n" +
            "Рекомендуется сначала сделать экспорт текущей базы.\n\n" +
            "Продолжить?",
            "Подтверждение импорта",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        try
        {
            var count = _db.ImportFromCsv(openDialog.FileName);
            LoadPlayers();
            ResultText = $"📥 Импорт завершён: обработано {count} записей.";
        }
        catch (Exception ex)
        {
            ResultText = $"⚠ Ошибка импорта: {ex.Message}";
        }
    }

    /// <summary>Открывает окно ввода секретного кода и выполняет действие.</summary>
    public void ShowSecretCodeDialog()
    {
        var win = new FortuneWheel.Views.SecretCodeWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (win.ShowDialog() != true) return;

        var code = win.EnteredCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return;

        ExecuteSecretCode(code);
    }

    /// <summary>Выполняет действие по секретному коду.</summary>
    private void ExecuteSecretCode(string code)
    {
        switch (code)
        {
            case "ПОМОЩЬ":
            case "HELP":
                MessageBox.Show(
                    "Доступные секретные коды:\n\n" +
                    "• СБРОС — сбросить все коэффициенты до 1\n" +
                    "• СТАТА — сбросить статистику (участия/победы)\n" +
                    "• ОБНУЛИТЬ — полностью очистить базу данных\n" +
                    "• ПОМОЩЬ — показать это окно\n\n" +
                    "Горячая клавиша: Ctrl+Shift+K",
                    "Секретные команды",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                ResultText = "🔐 Выведен список секретных команд.";
                break;

            case "СБРОС":
            case "RESET_COEF":
                var res1 = MessageBox.Show(
                    "Сбросить все коэффициенты до 1?\n\n" +
                    "Статистика (участия/победы) останется нетронутой.",
                    "Подтверждение сброса коэффициентов",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (res1 != MessageBoxResult.Yes) return;

                var count1 = _db.ResetCoefficients();
                LoadPlayers();
                ResultText = $"🔄 Коэффициенты сброшены у {count1} сотрудников.";
                break;

            case "СТАТА":
            case "RESET_STATS":
                var res2 = MessageBox.Show(
                    "Сбросить статистику (участия и победы)?\n\n" +
                    "Коэффициенты и список сотрудников останутся нетронутыми.",
                    "Подтверждение сброса статистики",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (res2 != MessageBoxResult.Yes) return;

                _db.ResetStatistics();
                LoadPlayers();
                ResultText = "📊 Статистика сброшена.";
                break;

            case "ОБНУЛИТЬ":
            case "RESET_ALL":
                var res3 = MessageBox.Show(
                    "ПОЛНОСТЬЮ очистить базу данных?\n\n" +
                    "⚠ Будут удалены:\n" +
                    "• Все сотрудники\n" +
                    "• Вся история розыгрышей\n" +
                    "• Вся статистика\n\n" +
                    "Это действие необратимо!",
                    "ПОДТВЕРЖДЕНИЕ ПОЛНОГО СБРОСА",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (res3 != MessageBoxResult.Yes) return;

                // Второе подтверждение для надёжности
                var res4 = MessageBox.Show(
                    "Вы ТОЧНО уверены?\nПоследний шанс отменить.",
                    "Финальное подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                if (res4 != MessageBoxResult.Yes) return;

                _db.ResetAll();
                LoadPlayers();
                ResultText = "💥 База данных полностью очищена.";
                break;

            default:
                MessageBox.Show(
                    $"Неизвестный код: «{code}»\n\nПопробуйте «ПОМОЩЬ».",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ResultText = $"🔐 Неверный код: {code}";
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}