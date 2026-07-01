using System.IO;
using System.Windows;
using FortuneWheel.Services;
using FortuneWheel.ViewModels;

namespace FortuneWheel;

public partial class App : Application
{
    /// <summary>Режим работы приложения.</summary>
    public enum StorageMode
    {
        /// <summary>Данные в %AppData% (обычная установка).</summary>
        AppData,
        /// <summary>Данные рядом с exe (портативный режим).</summary>
        Portable
    }

    public static StorageMode CurrentMode { get; private set; }
    public static string DatabasePath { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dbPath;
        try
        {
            (CurrentMode, dbPath) = DetermineStorageMode();
            DatabasePath = dbPath;
        }
        catch (Exception ex)
        {
            ShowFatalError("Не удалось определить режим хранения данных.\n\n" + ex.Message);
            return;
        }

        DbService db;
        try
        {
            db = new DbService(dbPath);
            db.Init();
        }
        catch (Exception ex)
        {
            ShowFatalError($"Не удалось инициализировать базу данных в режиме {CurrentMode}.\n\n" + ex.Message);
            return;
        }

        var vm = new MainViewModel(db, dbPath, CurrentMode);
        var win = new MainWindow { DataContext = vm };
        win.Show();
    }

    /// <summary>
    /// Определяет, где хранить базу данных.
    /// </summary>
    /// <returns>Режим и путь к БД.</returns>
    private static (StorageMode mode, string dbPath) DetermineStorageMode()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            throw new InvalidOperationException("Не удалось определить путь к exe-файлу.");

        var exeFolder = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException("Не удалось получить папку exe.");

        // --- Проверка 1: принудительный portable-режим ---
        var portableMarker = Path.Combine(exeFolder, "portable.ini");
        if (File.Exists(portableMarker))
        {
            var dbPath = Path.Combine(exeFolder, "fortune.db");
            return (StorageMode.Portable, dbPath);
        }

        // --- Проверка 2: автоматическое определение по типу диска ---
        var driveInfo = new DriveInfo(Path.GetPathRoot(exePath) ?? "C:\\");
        if (driveInfo.DriveType == DriveType.Removable)
        {
            var dbPath = Path.Combine(exeFolder, "fortune.db");
            return (StorageMode.Portable, dbPath);
        }

        // --- Проверка 3: обычный режим (%AppData%) ---
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FortuneWheel");
        Directory.CreateDirectory(appDataFolder);
        var appDataDbPath = Path.Combine(appDataFolder, "fortune.db");
        return (StorageMode.AppData, appDataDbPath);
    }

    private static void ShowFatalError(string message)
    {
        MessageBox.Show(
            message,
            "Ошибка запуска «Колеса Фортуны»",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Current.Shutdown(1);
    }
}