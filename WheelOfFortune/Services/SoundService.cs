using System.IO;
using System.Reflection;
using System.Windows.Media;

namespace FortuneWheel.Services;

/// <summary>
/// Сервис для воспроизведения звуков. Загружает файлы из ресурсов приложения.
/// </summary>
public sealed class SoundService
{
    private readonly Random _random = new();

    // Массивы звуков для рандомного выбора
    private readonly List<MediaPlayer> _spinStartSounds = new();
    private readonly List<MediaPlayer> _tickSounds = new();
    private readonly List<MediaPlayer> _winSounds = new();

    public SoundService()
    {
        LoadSounds();
    }

    /// <summary>Загружает все звуки из ресурсов.</summary>
    private void LoadSounds()
    {
        try
        {
            // Звук начала розыгрыша
            _spinStartSounds.Add(LoadSound("spin_start.wav"));

            // Звуки переключения (tick1.wav, tick2.wav, tick3.wav)
            for (int i = 1; i <= 3; i++)
            {
                _tickSounds.Add(LoadSound($"tick{i}.wav"));
            }

            // Звуки победы (win1.wav, win2.wav)
            for (int i = 1; i <= 2; i++)
            {
                _winSounds.Add(LoadSound($"win{i}.wav"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки звуков: {ex.Message}");
        }
    }

    /// <summary>Загружает один звуковой файл из ресурсов.</summary>
    private MediaPlayer LoadSound(string fileName)
    {
        var player = new MediaPlayer();

        // Пытаемся загрузить из ресурсов (для single-file exe)
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"FortuneWheel.Sounds.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            // Для MediaPlayer нужно сохранить во временный файл
            var tempPath = Path.Combine(Path.GetTempPath(), $"fw_{fileName}");
            using var fileStream = File.Create(tempPath);
            stream.CopyTo(fileStream);
            player.Open(new Uri(tempPath, UriKind.Absolute));
        }
        else
        {
            // Если не в ресурсах — пытаемся загрузить из папки Sounds рядом с exe
            var exePath = Environment.ProcessPath;
            var exeFolder = Path.GetDirectoryName(exePath) ?? ".";
            var filePath = Path.Combine(exeFolder, "Sounds", fileName);

            if (File.Exists(filePath))
            {
                player.Open(new Uri(filePath, UriKind.Absolute));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Звуковой файл не найден: {filePath}");
            }
        }

        return player;
    }

    /// <summary>Воспроизводит звук начала розыгрыша.</summary>
    public void PlaySpinStart()
    {
        PlayRandom(_spinStartSounds);
    }

    /// <summary>Воспроизводит случайный звук переключения.</summary>
    public void PlayTick()
    {
        PlayRandom(_tickSounds);
    }

    /// <summary>Воспроизводит случайный звук победы.</summary>
    public void PlayWin()
    {
        PlayRandom(_winSounds);
    }

    /// <summary>Воспроизводит случайный звук из списка.</summary>
    private void PlayRandom(List<MediaPlayer> sounds)
    {
        if (sounds.Count == 0) return;

        var sound = sounds[_random.Next(sounds.Count)];
        sound.Stop();
        sound.Position = TimeSpan.Zero;
        sound.Play();
    }
}
