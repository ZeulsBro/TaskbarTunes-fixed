using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using TaskbarTunes.Controls;
using TaskbarTunes.Helpers;
using TaskbarTunes.Services;

namespace TaskbarTunes;

/// <summary>
/// Panel expandido: carátula grande, título completo, barra de tiempo con seek
/// y controles. Se abre con un clic en el widget y se cierra al perder el foco.
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _timer;
    private bool _seeking;
    private bool _changingVolume;
    private readonly DispatcherTimer _volumeTimer;

    public FlyoutWindow(SettingsService settings, MediaSessionService media)
    {
        InitializeComponent();
        _settings = settings;
        _media = media;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => { RefreshTimeline(); RefreshVolume(); };
        _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

        _media.TrackChanged += OnTrack;
        Deactivated += (_, _) => HideFlyout();

        SeekSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _seeking = true));
        SeekSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) =>
            {
                _media.SeekToFraction(SeekSlider.Value);
                _seeking = false;
            }));
        // Clic directo en la pista del slider (sin arrastrar el pulgar)
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_seeking) _media.SeekToFraction(SeekSlider.Value);
        };

        VolumeSlider.ValueChanged += (_, _) =>
        {
            if (_changingVolume) return;
            VolumeText.Text = $"{(int)Math.Round(VolumeSlider.Value * 100)}%";
            if (VolumeSlider.IsMouseCaptureWithin || VolumeSlider.IsKeyboardFocusWithin)
                SetYandexVolume(VolumeSlider.Value);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TaskbarPositioner.MakeToolWindow(this, noActivate: false); // necesita foco para autocerrarse
    }

    public void ShowFlyout(double leftDip, double topDip)
    {
        ApplyAppearance();
        UpdateTrackUi(_media.LastTrack);
        RefreshTimeline();

        Left = leftDip;
        Top = topDip;
        Show();
        Activate();
        _timer.Start();
        RefreshVolume();
    }

    public void HideFlyout()
    {
        _timer.Stop();
        Hide();
    }

    private void ApplyAppearance()
    {
        var s = _settings.Settings;
        var bg = AudioVisualizer.ParseColor(s.BackgroundColor, Color.FromArgb(0xF0, 0x20, 0x20, 0x20));
        // El panel necesita más opacidad que el widget para leerse bien
        var solid = Color.FromArgb(Math.Max(bg.A, (byte)0xE8), bg.R, bg.G, bg.B);

        if (s.AcrylicBackground)
        {
            AcrylicHelper.Apply(this, true, Color.FromArgb(0xCC, bg.R, bg.G, bg.B));
            FlyRoot.Background = Brushes.Transparent;
        }
        else
        {
            AcrylicHelper.Apply(this, false, solid);
            FlyRoot.Background = new SolidColorBrush(solid);
        }

        var text = new SolidColorBrush(AudioVisualizer.ParseColor(s.TextColor, Colors.White));
        text.Freeze();
        FlyTitle.Foreground = text;
        FlyArtist.Foreground = text;
        FlySource.Foreground = text;
        FlyArtPlaceholder.Foreground = text;
        PosText.Foreground = text;
        DurText.Foreground = text;
        FlyPrev.Foreground = text;
        FlyPlay.Foreground = text;
        FlyNext.Foreground = text;

        var font = new FontFamily($"{s.FontFamily}, Segoe UI");
        FlyTitle.FontFamily = font;
        FlyArtist.FontFamily = font;
    }

    private void OnTrack(TrackInfo? info)
    {
        if (IsVisible) UpdateTrackUi(info);
    }

    private void UpdateTrackUi(TrackInfo? info)
    {
        if (info is null)
        {
            FlyTitle.Text = "Sin reproducción";
            FlyArtist.Text = "";
            FlySource.Text = "";
            FlyArt.Source = null;
            FlyArtPlaceholder.Visibility = Visibility.Visible;
            FlyPlay.Content = ""; // play
        }
        else
        {
            FlyTitle.Text = string.IsNullOrWhiteSpace(info.Title) ? "(Sin título)" : info.Title;
            FlyArtist.Text = info.Artist;
            FlySource.Text = info.SourceName;
            FlyArt.Source = info.AlbumArt;
            FlyArtPlaceholder.Visibility = info.AlbumArt is null ? Visibility.Visible : Visibility.Collapsed;
            FlyPlay.Content = info.IsPlaying ? "" : ""; // pausa / play
        }
    }

    private void RefreshTimeline()
    {
        var snap = _media.GetTimelineSnapshot();
        if (snap is null)
        {
            SeekSlider.IsEnabled = false;
            if (!_seeking) SeekSlider.Value = 0;
            PosText.Text = "--:--";
            DurText.Text = "--:--";
            return;
        }

        var (fraction, position, duration) = snap.Value;
        SeekSlider.IsEnabled = true;
        if (!_seeking) SeekSlider.Value = fraction;
        PosText.Text = Format(position);
        DurText.Text = Format(duration);
    }

    // Изменяет громкость только аудиосессии приложения Яндекс Музыка, если Windows
    // предоставляет её отдельно. Другие приложения не затрагиваются.
    private void RefreshVolume()
    {
        if (VolumeSlider.IsMouseCaptureWithin || VolumeSlider.IsKeyboardFocusWithin) return;
        var volume = GetYandexVolume();
        _changingVolume = true;
        VolumeSlider.IsEnabled = volume.HasValue;
        if (volume.HasValue) VolumeSlider.Value = Math.Clamp(volume.Value, 0, 1);
        VolumeText.Text = volume.HasValue ? $"{(int)Math.Round(volume.Value * 100)}%" : "—";
        _changingVolume = false;
    }

    private static MMDevice? GetDefaultRenderDevice()
    {
        try { return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }
    }

    private static bool ContainsYandexMusic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("yandex", StringComparison.OrdinalIgnoreCase)
            || value.Contains("яндекс", StringComparison.OrdinalIgnoreCase)
            || value.Contains("y.music", StringComparison.OrdinalIgnoreCase)
            || value.Contains("music", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ru.yandex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYandexSession(AudioSessionControl session)
    {
        try
        {
            // Some Yandex Music builds expose a generic/helper process name,
            // while the audio session itself carries the app name or identifier.
            if (ContainsYandexMusic(session.DisplayName) ||
                ContainsYandexMusic(session.GetSessionIdentifier))
                return true;

            int processId = (int)session.GetProcessID;
            if (processId == 0) return false;
            try
            {
                using var process = Process.GetProcessById(processId);
                if (ContainsYandexMusic(process.ProcessName)) return true;
                try
                {
                    var fileName = process.MainModule?.FileName;
                    if (ContainsYandexMusic(fileName)) return true;
                }
                catch { /* Windows may deny access to another process path */ }
            }
            catch { }
        }
        catch { }
        return false;
    }

    private static float? GetYandexVolume()
    {
        MMDevice? device = null;
        try
        {
            device = GetDefaultRenderDevice();
            if (device is null) return null;
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (IsYandexSession(session))
                    return session.SimpleAudioVolume.Volume;
            }
        }
        catch { }
        finally { device?.Dispose(); }
        return null;
    }

    private static void SetYandexVolume(double value)
    {
        MMDevice? device = null;
        try
        {
            device = GetDefaultRenderDevice();
            if (device is null) return;
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (IsYandexSession(session))
                    session.SimpleAudioVolume.Volume = (float)Math.Clamp(value, 0, 1);
            }
        }
        catch { }
        finally { device?.Dispose(); }
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    private void OnPlayPause(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void OnPrev(object sender, RoutedEventArgs e) => _media.SkipPrevious();
    private void OnNext(object sender, RoutedEventArgs e) => _media.SkipNext();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) HideFlyout();
    }

    protected override void OnClosed(EventArgs e)
    {
        _media.TrackChanged -= OnTrack;
        base.OnClosed(e);
    }
}
