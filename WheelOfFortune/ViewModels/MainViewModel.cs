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

    private bool _isSoundEnabled = true;

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

    public bool IsSoundEnabled
    {
        get => _isSoundEnabled;
        set
        {
            if (_isSoundEnabled != value)
            {
                _isSoundEnabled = value;
                _sound.IsEnabled = value;
                OnPropertyChanged();
            }
        }
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

    /// <summary>Анимация «бегущей подсветки» с постепенным замедлением (ускоренная версия).</summary>
    private async Task AnimateWheelAsync(List<PlayerViewModel> participants)
    {
        var rng = new Random();
        int totalSteps = 16 + rng.Next(6); // было 28-38, стало 16-22
        try
        {
            for (int i = 0; i < totalSteps; i++)
            {
                var target = participants[rng.Next(participants.Count)];
                foreach (var p in participants) p.IsHighlighted = false;
                target.IsHighlighted = true;
                _sound.PlayTick();

                // Ускоренная кривая: от ~30 мс до ~350 мс (было 60-600)
                int delay = 30 + i * i * 2;
                await Task.Delay(Math.Min(delay, 350));
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

        // Подписываемся на событие "Применить" — выполняет код без закрытия окна
        win.ApplyRequested += (_, code) =>
        {
            ExecuteSecretCode(code);
        };

        if (win.ShowDialog() != true) return;

        // Если нажали OK — выполняем код (окно уже закроется)
        var code = win.EnteredCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return;
        ExecuteSecretCode(code);
    }

    /// <summary>Выполняет действие по секретному коду.</summary>
    private void ExecuteSecretCode(string code)
    {
        // Разбираем код с параметрами (например, "ТЕСТ 100")
        var parts = code.Split(' ', 2);
        var command = parts[0];
        var parameter = parts.Length > 1 ? parts[1] : null;

        switch (command)
        {
            case "ПОМОЩЬ":
            case "HELP":
                MessageBox.Show(
                    "Доступные секретные коды:\n\n" +
                    "• СБРОС — сбросить все коэффициенты до 1\n" +
                    "• СТАТА — сбросить статистику (участия/победы)\n" +
                    "• ОБНУЛИТЬ — полностью очистить базу данных\n" +
                    "• ЭКСПОРТ — быстрый экспорт в папку с БД\n" +
                    "• РЕЖИМ — переключить режим хранения (Portable/AppData)\n" +
                    "• ЗВУК ON / ЗВУК OFF — включить/выключить звуки\n" +
                    "• ТЕСТ N — запустить N розыгрышей для проверки (например, ТЕСТ 100)\n" +
                    "• ПОМОЩЬ — показать это окно\n\n" +
                    "Горячая клавиша: Ctrl+Shift+K\n" +
                    "Кнопка «Применить» — выполнить команду без закрытия окна",
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

            case "ЭКСПОРТ":
            case "EXPORT":
                try
                {
                    var csv = _db.ExportToCsv();
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var fileName = $"fortune_backup_{timestamp}.csv";
                    var dbFolder = Path.GetDirectoryName(_dbPath) ?? ".";
                    var filePath = Path.Combine(dbFolder, fileName);
                    File.WriteAllText(filePath, csv, System.Text.Encoding.UTF8);
                    ResultText = $"💾 Быстрый экспорт выполнен: {filePath}";
                    MessageBox.Show(
                        $"База экспортирована в:\n{filePath}",
                        "Экспорт завершён",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ResultText = $"⚠ Ошибка экспорта: {ex.Message}";
                }
                break;

            case "РЕЖИМ":
            case "MODE":
                SwitchStorageMode();
                break;

            case "ЗВУК":
            case "SOUND":
                if (parameter == "ON")
                {
                    _sound.IsEnabled = true;
                    ResultText = "🔊 Звуки включены.";
                }
                else if (parameter == "OFF")
                {
                    _sound.IsEnabled = false;
                    ResultText = "🔇 Звуки выключены.";
                }
                else
                {
                    MessageBox.Show(
                        "Используйте: ЗВУК ON или ЗВУК OFF",
                        "Неверный параметр",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                break;

            case "ТЕСТ":
            case "TEST":
                if (parameter != null && int.TryParse(parameter, out int testCount))
                {
                    RunTestSpins(testCount);
                }
                else
                {
                    MessageBox.Show(
                        "Используйте: ТЕСТ N\nгде N — количество розыгрышей (например, ТЕСТ 100)",
                        "Неверный параметр",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                break;

            default:
                MessageBox.Show(
                    $"Неизвестный код: «{command}»\n\nПопробуйте «ПОМОЩЬ».",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ResultText = $"🔐 Неверный код: {command}";
                break;
        }
    }


    /// <summary>Переключает режим хранения данных.</summary>
    private void SwitchStorageMode()
    {
        var newMode = _storageMode == App.StorageMode.Portable
            ? App.StorageMode.AppData
            : App.StorageMode.Portable;

        var res = MessageBox.Show(
            $"Переключить режим хранения?\n\n" +
            $"Текущий: {_storageMode}\n" +
            $"Новый: {newMode}\n\n" +
            $"База данных будет перенесена в новое расположение.",
            "Смена режима",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        try
        {
            string newDbPath;
            if (newMode == App.StorageMode.Portable)
            {
                var exePath = Environment.ProcessPath;
                var exeFolder = Path.GetDirectoryName(exePath) ?? ".";
                newDbPath = Path.Combine(exeFolder, "fortune.db");
            }
            else
            {
                var appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FortuneWheel");
                Directory.CreateDirectory(appDataFolder);
                newDbPath = Path.Combine(appDataFolder, "fortune.db");
            }

            if (File.Exists(_dbPath))
            {
                File.Copy(_dbPath, newDbPath, overwrite: true);
            }

            var exePath2 = Environment.ProcessPath;
            var exeFolder2 = Path.GetDirectoryName(exePath2) ?? ".";
            var portableMarker = Path.Combine(exeFolder2, "portable.ini");

            if (newMode == App.StorageMode.Portable && !File.Exists(portableMarker))
            {
                File.WriteAllText(portableMarker, "");
            }
            else if (newMode == App.StorageMode.AppData && File.Exists(portableMarker))
            {
                File.Delete(portableMarker);
            }

            ResultText = $"🔄 Режим изменён на {newMode}. Перезапустите приложение.";
            MessageBox.Show(
                $"Режим хранения изменён на {newMode}.\n\n" +
                $"Перезапустите приложение для применения изменений.",
                "Режим изменён",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ResultText = $"⚠ Ошибка смены режима: {ex.Message}";
        }
    }

    /// <summary>Запускает N розыгрышей подряд для проверки алгоритма.</summary>
    private async void RunTestSpins(int count)
    {
        if (count <= 0 || count > 10000)
        {
            MessageBox.Show(
                "Количество должно быть от 1 до 10000.",
                "Неверный параметр",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show(
            $"Запустить {count} розыгрышей без анимации?\n\n" +
            $"Это полезно для проверки распределения побед.",
            "Тестовый режим",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res != MessageBoxResult.Yes) return;

        IsSpinning = true;
        ResultText = $"🧪 Тест: запускаем {count} розыгрышей...";

        var participantIds = Players.Where(p => p.IsSelected).Select(p => p.Id).ToList();

        if (participantIds.Count == 0)
        {
            ResultText = "⚠ Отметьте хотя бы одного участника.";
            IsSpinning = false;
            return;
        }

        // Словари для статистики
        var wins = new Dictionary<int, int>();
        var winCoefSum = new Dictionary<int, double>();
        var winCoefMin = new Dictionary<int, int>();
        var winCoefMax = new Dictionary<int, int>();
        var coefRanges = new Dictionary<string, int>
        {
            ["0-5"] = 0,
            ["6-10"] = 0,
            ["11-15"] = 0,
            ["16-20"] = 0,
            ["21+"] = 0
        };

        await Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
            {
                var allModels = Players.Select(p => p.Model).ToList();
                var (winner, coefAtWin) = _wheel.Spin(allModels, participantIds);
                if (winner != null)
                {
                    if (!wins.ContainsKey(winner.Id))
                    {
                        wins[winner.Id] = 0;
                        winCoefSum[winner.Id] = 0;
                        winCoefMin[winner.Id] = int.MaxValue;
                        winCoefMax[winner.Id] = int.MinValue;
                    }
                    wins[winner.Id]++;
                    winCoefSum[winner.Id] += coefAtWin;
                    winCoefMin[winner.Id] = Math.Min(winCoefMin[winner.Id], coefAtWin);
                    winCoefMax[winner.Id] = Math.Max(winCoefMax[winner.Id], coefAtWin);

                    // Распределяем по диапазонам
                    if (coefAtWin <= 5) coefRanges["0-5"]++;
                    else if (coefAtWin <= 10) coefRanges["6-10"]++;
                    else if (coefAtWin <= 15) coefRanges["11-15"]++;
                    else if (coefAtWin <= 20) coefRanges["16-20"]++;
                    else coefRanges["21+"]++;
                }

                // Обновляем коэффициенты в моделях
                foreach (var vm in Players)
                {
                    var model = allModels.First(m => m.Id == vm.Id);
                    vm.Model.Coefficient = model.Coefficient;
                }
            }
        });

        // Формируем отчёт
        var report = new System.Text.StringBuilder();

        // Заголовок
        report.AppendLine("═══════════════════════════════════════════════════════════");
        report.AppendLine($"  🧪 РЕЗУЛЬТАТ ТЕСТИРОВАНИЯ АЛГОРИТМА");
        report.AppendLine($"  Всего розыгрышей: {count}");
        report.AppendLine($"  Участников: {participantIds.Count}");
        report.AppendLine("═══════════════════════════════════════════════════════════");
        report.AppendLine();

        // Таблица результатов
        report.AppendLine("┌────────────────────┬────────┬──────────┬──────────┬─────────────┬─────────────┐");
        report.AppendLine("│ Сотрудник          │ Побед  │ % побед  │ Ср. коэф │ Мин. коэф   │ Макс. коэф  │");
        report.AppendLine("├────────────────────┼────────┼──────────┼──────────┼─────────────┼─────────────┤");

        foreach (var vm in Players.Where(p => participantIds.Contains(p.Id)))
        {
            var winCount = wins.GetValueOrDefault(vm.Id, 0);
            var percent = (double)winCount / count * 100;
            var avgCoef = winCount > 0 ? winCoefSum[vm.Id] / winCount : 0;
            var minCoef = winCount > 0 ? winCoefMin[vm.Id] : 0;
            var maxCoef = winCount > 0 ? winCoefMax[vm.Id] : 0;

            var name = vm.Name.Length > 18 ? vm.Name[..18] + ".." : vm.Name.PadRight(18);

            report.AppendLine(
                $"│ {name} │ {winCount,6} │ {percent,7:F1}% │ {avgCoef,8:F2} │ {minCoef,11} │ {maxCoef,11} │");
        }

        report.AppendLine("└────────────────────┴────────┴──────────┴──────────┴─────────────┴─────────────┘");
        report.AppendLine();

        // Дополнительная аналитика
        report.AppendLine("───────────────────────────────────────────────────────────");
        report.AppendLine("📈 ДОПОЛНИТЕЛЬНАЯ АНАЛИТИКА");
        report.AppendLine("───────────────────────────────────────────────────────────");

        // Кто чаще всех выигрывал
        if (wins.Count > 0)
        {
            var topWinner = wins.OrderByDescending(x => x.Value).First();
            var topWinnerVm = Players.First(p => p.Id == topWinner.Key);
            var topPercent = (double)topWinner.Value / count * 100;
            report.AppendLine($"🏆 Лидер по победам: {topWinnerVm.Name} ({topWinner.Value} побед, {topPercent:F1}%)");
        }

        // Кто ни разу не выиграл
        var losers = Players.Where(p => participantIds.Contains(p.Id) && !wins.ContainsKey(p.Id)).ToList();
        if (losers.Count > 0)
        {
            report.AppendLine($"😢 Ни разу не выиграли: {string.Join(", ", losers.Select(l => l.Name))}");
        }

        // Распределение по диапазонам коэффициентов
        report.AppendLine();
        report.AppendLine("📊 Распределение побед по диапазонам коэффициентов:");
        foreach (var range in coefRanges)
        {
            var percent = (double)range.Value / count * 100;
            var bar = new string('█', (int)(percent / 2)); // визуальная шкала
            report.AppendLine($"   {range.Key,-6} : {range.Value,5} ({percent,5:F1}%) {bar}");
        }

        // Общий средний коэффициент при победе
        var totalAvgCoef = winCoefSum.Values.Sum() / Math.Max(1, wins.Values.Sum());
        report.AppendLine();
        report.AppendLine($"📐 Общий средний коэффициент при победе: {totalAvgCoef:F2}");

        // Справедливость: стандартное отклонение
        if (wins.Count > 1)
        {
            var avgWins = (double)wins.Values.Sum() / wins.Count;
            var variance = wins.Values.Average(w => Math.Pow(w - avgWins, 2));
            var stdDev = Math.Sqrt(variance);
            var cv = avgWins > 0 ? stdDev / avgWins * 100 : 0;

            report.AppendLine($"⚖️  Коэффициент вариации: {cv:F1}%");
            if (cv < 15)
                report.AppendLine("   → Отличная справедливость (почти равное распределение)");
            else if (cv < 30)
                report.AppendLine("   → Хорошая справедливость");
            else if (cv < 50)
                report.AppendLine("   → Умеренная несправедливость");
            else
                report.AppendLine("   → Высокая несправедливость (кто-то выигрывает намного чаще)");
        }

        report.AppendLine();
        report.AppendLine("═══════════════════════════════════════════════════════════");

        // Сбрасываем коэффициенты после теста
        _db.ResetCoefficients();
        LoadPlayers();

        // Создаём окно для отображения отчёта с моноширинным шрифтом
        var resultWindow = new Window
        {
            Title = "Результат теста",
            Width = 750,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current.MainWindow,
            Background = System.Windows.Media.Brushes.White
        };

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = report.ToString(),
            IsReadOnly = true,
            TextWrapping = System.Windows.TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Padding = new System.Windows.Thickness(10),
            BorderThickness = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.White
        };

        resultWindow.Content = textBox;
        resultWindow.ShowDialog();

        ResultText = $"🧪 Тест завершён: {count} розыгрышей.";
        IsSpinning = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}