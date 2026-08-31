using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace tomat
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? _smtc;
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private DispatcherTimer _hideTimer;

        private readonly List<GlobalSystemMediaTransportControlsSession> _trackedSessions = new();

        public MainWindow()
        {
            InitializeComponent();

            Left = 20;
            Top = 20;

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += (s, e) => { Hide(); _hideTimer.Stop(); };

            Loaded += (s, e) => Hide();

            SMTC_Initialize();
        }
        private int WM_SHELLHOOK;
        private const int HSHELL_APPCOMMAND = 12;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);

            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            WM_SHELLHOOK = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(helper.Handle);

            HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SHELLHOOK && wParam.ToInt32() == HSHELL_APPCOMMAND)
            {
                int cmd = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                if (cmd >= 11 && cmd <= 14)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                    {
                        UpdateAll(showFlyout: true);
                    }));
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            DeregisterShellHookWindow(helper.Handle);
            base.OnClosed(e);
        }

        private void SMTC_Initialize()
        {
            _smtc = GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();

            if (_smtc != null)
            {
                _smtc.SessionsChanged += OnGlobalSessionsChanged;
                UpdateAll(showFlyout: false);
            }
        }

        private void OnGlobalSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                UpdateAll(showFlyout: false);
            }));
        }

        private void OnSessionMediaOrPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                UpdateAll(showFlyout: false);
            }));
        }

        private async void UpdateAll(bool showFlyout)
        {
            await _updateLock.WaitAsync();
            try
            {
                if (_smtc == null) return;

                var allSessions = _smtc.GetSessions().ToList();
                if (!allSessions.Any())
                {
                    MediaSessionsContainer.Children.Clear();
                    return;
                }

                var validSessions = new List<(GlobalSystemMediaTransportControlsSession Session, string Title, bool IsPlaying)>();

                foreach (var session in allSessions)
                {
                    try
                    {
                        var props = await session.TryGetMediaPropertiesAsync();
                        var info = session.GetPlaybackInfo();

                        if (props != null && !string.IsNullOrWhiteSpace(props.Title))
                        {
                            bool isPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            validSessions.Add((session, props.Title, isPlaying));
                        }
                    }
                    catch { }
                }

                var uniqueSessions = validSessions
                    .GroupBy(x => x.Session.SourceAppUserModelId)
                    .Select(g => g.OrderByDescending(x => x.IsPlaying).First().Session)
                    .ToList();

                List<UIElement> newRows = new();
                foreach (var session in uniqueSessions)
                {
                    session.MediaPropertiesChanged -= OnSessionMediaOrPlaybackChanged;
                    session.PlaybackInfoChanged -= OnSessionMediaOrPlaybackChanged;
                    session.MediaPropertiesChanged += OnSessionMediaOrPlaybackChanged;
                    session.PlaybackInfoChanged += OnSessionMediaOrPlaybackChanged;

                    var sessionRow = await BuildSessionRowAsync(session);
                    if (sessionRow != null)
                    {
                        newRows.Add(sessionRow);
                    }
                }

                MediaSessionsContainer.Children.Clear();
                foreach (var row in newRows)
                {
                    MediaSessionsContainer.Children.Add(row);
                }

                if (newRows.Count > 0 && showFlyout)
                {
                    TriggerFlyout();
                }
            }
            finally
            {
                _updateLock.Release();
            }
        }

        private async Task<UIElement?> BuildSessionRowAsync(GlobalSystemMediaTransportControlsSession session)
        {
            var mediaProps = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();

            if (mediaProps == null || string.IsNullOrEmpty(mediaProps.Title)) return null;

            Grid row = new() { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            Border imgBorder = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(4), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            Image img = new() { Stretch = Stretch.UniformToFill };

            if (mediaProps.Thumbnail != null)
            {
                try
                {
                    using var winrtStream = await mediaProps.Thumbnail.OpenReadAsync();
                    using var dataReader = new DataReader(winrtStream);
                    await dataReader.LoadAsync((uint)winrtStream.Size);

                    byte[] buffer = new byte[(int)winrtStream.Size];
                    dataReader.ReadBytes(buffer);

                    using var ms = new MemoryStream(buffer);
                    BitmapImage bitmap = new();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    img.Source = bitmap;
                }
                catch { }
            }

            imgBorder.Child = img;
            Grid.SetColumn(imgBorder, 0);
            row.Children.Add(imgBorder);

            StackPanel textPanel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            StackPanel headerPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

            ImageSource? appIcon = GetAppIcon(session.SourceAppUserModelId);
            if (appIcon != null)
            {
                Image iconImg = new()
                {
                    Source = appIcon,
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(iconImg);
            }

            TextBlock appName = new()
            {
                Text = CleanAppName(session.SourceAppUserModelId),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(appName);

            TextBlock title = new() { Text = mediaProps.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            TextBlock artist = new() { Text = string.IsNullOrEmpty(mediaProps.Artist) ? "Неизвестен" : mediaProps.Artist, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };

            textPanel.Children.Add(headerPanel);
            textPanel.Children.Add(title);
            textPanel.Children.Add(artist);
            Grid.SetColumn(textPanel, 1);
            row.Children.Add(textPanel);

            StackPanel btnPanel = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            Button btnPrev = CreateControlButton("⏮");
            Button btnPlay = CreateControlButton(isPlaying ? "⏸" : "▶");
            Button btnNext = CreateControlButton("⏭");

            btnPrev.Click += async (s, e) => { await session.TrySkipPreviousAsync(); };
            btnPlay.Click += async (s, e) => { await session.TryTogglePlayPauseAsync(); };
            btnNext.Click += async (s, e) => { await session.TrySkipNextAsync(); };

            btnPanel.Children.Add(btnPrev);
            btnPanel.Children.Add(btnPlay);
            btnPanel.Children.Add(btnNext);
            Grid.SetColumn(btnPanel, 2);
            row.Children.Add(btnPanel);

            return row;
        }

        private static Button CreateControlButton(string content)
        {
            return new Button
            {
                Content = content,
                Width = 36,
                Height = 36,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13
            };
        }

        private static string CleanAppName(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return "Плеер";
            if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (appId.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";
            if (appId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";

            var parts = appId.Split('!', '\\');
            string name = parts.Last().Replace(".exe", "");
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static ImageSource? GetAppIcon(string appId)
        {
            try
            {
                string exeName = appId.Split('!', '\\').Last();
                if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeName += ".exe";

                var process = System.Diagnostics.Process.GetProcessesByName(exeName.Replace(".exe", "")).FirstOrDefault();
                if (process?.MainModule?.FileName != null)
                {
                    IntPtr hIcon = ExtractIcon(IntPtr.Zero, process.MainModule.FileName, 0);
                    if (hIcon != IntPtr.Zero && hIcon != 1)
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        DestroyIcon(hIcon);
                        return bitmapSource;
                    }
                }
            }
            catch { }
            return null;
        }

        private void TriggerFlyout()
        {
            Show();
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }
}