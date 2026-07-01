using System.IO;
using System.Reflection;
using System.Windows.Media;

namespace FortuneWheel.Services;

public sealed class SoundService
{
    private readonly Random _random = new();
    private readonly List<MediaPlayer> _spinStartSounds = new();
    private readonly List<MediaPlayer> _tickSounds = new();
    private readonly List<MediaPlayer> _winSounds = new();

    /// <summary>Глобальный флаг: включены ли звуки.</summary>
    public bool IsEnabled { get; set; } = true;

    public SoundService()
    {
        LoadSounds();
    }

    private void LoadSounds()
    {
        try
        {
            _spinStartSounds.Add(LoadSound("spin_start.wav"));
            for (int i = 1; i <= 3; i++)
                _tickSounds.Add(LoadSound($"tick{i}.wav"));
            for (int i = 1; i <= 2; i++)
                _winSounds.Add(LoadSound($"win{i}.wav"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки звуков: {ex.Message}");
        }
    }

    private MediaPlayer LoadSound(string fileName)
    {
        var player = new MediaPlayer();
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"FortuneWheel.Sounds.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"fw_{fileName}");
            using var fileStream = File.Create(tempPath);
            stream.CopyTo(fileStream);
            player.Open(new Uri(tempPath, UriKind.Absolute));
        }
        else
        {
            var exePath = Environment.ProcessPath;
            var exeFolder = Path.GetDirectoryName(exePath) ?? ".";
            var filePath = Path.Combine(exeFolder, "Sounds", fileName);
            if (File.Exists(filePath))
                player.Open(new Uri(filePath, UriKind.Absolute));
            else
                System.Diagnostics.Debug.WriteLine($"Звуковой файл не найден: {filePath}");
        }
        return player;
    }

    public void PlaySpinStart() => PlayRandom(_spinStartSounds);
    public void PlayTick() => PlayRandom(_tickSounds);
    public void PlayWin() => PlayRandom(_winSounds);

    private void PlayRandom(List<MediaPlayer> sounds)
    {
        if (!IsEnabled || sounds.Count == 0) return;
        var sound = sounds[_random.Next(sounds.Count)];
        sound.Stop();
        sound.Position = TimeSpan.Zero;
        sound.Play();
    }
}