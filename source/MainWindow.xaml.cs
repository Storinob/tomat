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
using System.Windows.Media.Animation;

namespace tomat
{
    public partial class MainWindow : Window
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> _iconCache = new();
        private GlobalSystemMediaTransportControlsSessionManager? _smtc;
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private DispatcherTimer _hideTimer;

        private readonly List<GlobalSystemMediaTransportControlsSession> _trackedSessions = new();

        public MainWindow()
        {
            InitializeComponent();
            Top = 20;
            //Left = 20;
            SizeChanged += (s, e) => 
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            };
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) }; // tiem bef hide
            _hideTimer.Tick += (s, e) => HideFlyout();
            //Loaded += (s, e) => Hide();
            Loaded += (s, e) => Hide();
            _ = SMTC_InitializeAsync();
        }
        private int WM_SHELLHOOK;
        private const int HSHELL_APPCOMMAND = 12;
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            _ = SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            WM_SHELLHOOK = RegisterWindowMessage("SHELLHOOK");
            _ = RegisterShellHookWindow(helper.Handle);
            HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SHELLHOOK && wParam.ToInt32() == HSHELL_APPCOMMAND)
            {
                int cmd = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                if (cmd >= 11 && cmd <= 14)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
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
            _ = DeregisterShellHookWindow(helper.Handle);
            base.OnClosed(e);
        }
        private async Task SMTC_InitializeAsync()
        {
            _smtc = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_smtc != null)
            {
                _smtc.SessionsChanged += OnGlobalSessionsChanged;
                UpdateAll(showFlyout: false);
            }
        }
        private void OnGlobalSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                UpdateAll(showFlyout: false);
            }));
        }
        private void OnSessionMediaOrPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
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
                if (newRows.Count > 0)
                {
                    foreach (var row in newRows)
                    {
                        MediaSessionsContainer.Children.Add(row);
                    }
                    if (showFlyout)
                    {
                        TriggerFlyout();
                    }
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
            byte[]? imgBytes = null;
            if (mediaProps.Thumbnail != null)
            {
                try
                {
                    using var winrtStream = await mediaProps.Thumbnail.OpenReadAsync();
                    using var dataReader = new DataReader(winrtStream);
                    _ = await dataReader.LoadAsync((uint)winrtStream.Size);
                    imgBytes = new byte[(int)winrtStream.Size];
                    dataReader.ReadBytes(imgBytes);
                }
                catch { }
            }
            Grid row = new() { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Колонке вернули размер под кнопки
            Border imgBorder = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(4), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            Image img = new() { Stretch = Stretch.UniformToFill };
            
            if (imgBytes != null && imgBytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(imgBytes);
                    BitmapImage bitmap = new();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    img.Source = bitmap;
                }
                catch { }
            }

            imgBorder.Child = img;
            Grid.SetColumn(imgBorder, 0);
            _ = row.Children.Add(imgBorder);
            StackPanel textPanel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            StackPanel headerPanel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            ImageSource? appIcon = GetAppIcon(session.SourceAppUserModelId);
            if (appIcon != null) // bruh
            {
                Image iconImg = new()
                {
                    Source = appIcon,
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _ = headerPanel.Children.Add(iconImg);
            }
            TextBlock appName = new()
            {
                Text = CleanAppName(session.SourceAppUserModelId),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            _ = headerPanel.Children.Add(appName);
            TextBlock title = new() { Text = mediaProps.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            TextBlock artist = new() { Text = string.IsNullOrEmpty(mediaProps.Artist) ? "Неизвестен" : mediaProps.Artist, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            _ = textPanel.Children.Add(headerPanel);
            _ = textPanel.Children.Add(title);
            _ = textPanel.Children.Add(artist);
            Grid.SetColumn(textPanel, 1);
            _ = row.Children.Add(textPanel);

            StackPanel btnPanel = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            
            Button btnPrev = CreateControlButton("⏮");
            Button btnPlay = CreateControlButton(isPlaying ? "⏸" : "▶");
            Button btnNext = CreateControlButton("⏭");

            btnPrev.Click += async (s, e) => { _ = Task.Run(async () => await session.TrySkipPreviousAsync()); };
            btnPlay.Click += async (s, e) => { _ = Task.Run(async () => await session.TryTogglePlayPauseAsync()); };
            btnNext.Click += async (s, e) => { _ = Task.Run(async () => await session.TrySkipNextAsync()); };

            _ = btnPanel.Children.Add(btnPrev);
            _ = btnPanel.Children.Add(btnPlay);
            _ = btnPanel.Children.Add(btnNext);
            Grid.SetColumn(btnPanel, 2);
            _ = row.Children.Add(btnPanel);

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
                Background = Brushes.Transparent,
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
            if (string.IsNullOrEmpty(appId)) return null;
            return _iconCache.GetOrAdd(appId, id =>
            {
                try
                {
                    string exeName = id.Split('!', '\\').Last();
                    if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeName += ".exe";
                    string processName = exeName.Replace(".exe", "");
                    var process = System.Diagnostics.Process.GetProcessesByName(processName).FirstOrDefault();
                    if (process?.MainModule?.FileName != null)
                    {
                        IntPtr hIcon = ExtractIcon(IntPtr.Zero, process.MainModule.FileName, 0);
                        if (hIcon != IntPtr.Zero && hIcon != (IntPtr)1)
                        {
                            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                hIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bitmapSource.Freeze(); 
                            DestroyIcon(hIcon);
                            return bitmapSource;
                        }
                    }
                }
                catch { }
                return null;
            });
        }
        private void TriggerFlyout()
        {
            if (IsVisible)
            {
                _hideTimer.Stop();
                _hideTimer.Start();
                return;
            }
            Show();
            _hideTimer.Stop();
            double targetTop = 20;
            double startTop = -ActualHeight - 20;
            DoubleAnimation animTop = new(fromValue: startTop, toValue: targetTop, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(Window.TopProperty, animTop);
            _hideTimer.Start();
        }
        private void HideFlyout()
        {
            if (!IsVisible) return;
            double targetTop = -ActualHeight - 20;
            DoubleAnimation animTop = new(toValue: targetTop, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            animTop.Completed += (s, e) =>
            {
                Hide();
                _hideTimer.Stop();
            };
            BeginAnimation(Window.TopProperty, animTop);
        }
    }
}