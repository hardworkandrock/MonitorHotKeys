using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MonitorHotKeys
{
    public partial class OverlayWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;
        private ProgressBar _progressBar;
        private bool _isFading = false;

        public OverlayWindow(string message, int autoCloseMs, bool isHoldOverlay = false, string customBackground = null, bool isHelpOverlay = false)
        {
            InitializeComponent();
            var grid = (Grid)Content;
            grid.Children.Clear();

            if (isHoldOverlay)
            {
                SetupHoldOverlay(grid, message, autoCloseMs);
            }
            else if (isHelpOverlay)
            {
                SetupHelpOverlay(grid, message);
            }
            else
            {
                SetupSimpleOverlay(grid, message, customBackground);
            }

            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromMilliseconds(autoCloseMs);
            _closeTimer.Tick += OnTimerTick;
            _closeTimer.Start();
        }
        private void SetupHoldOverlay(Grid grid, string message, int totalDurationMs)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                Padding = new Thickness(20),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stackPanel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            };
            stackPanel.Children.Add(textBlock);

            // Создаём ProgressBar отдельно и настраиваем свойства
            _progressBar = new ProgressBar
            {
                Width = 200,
                Height = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00))
            };
            _progressBar.Value = 0;
            _progressBar.Maximum = totalDurationMs;

            // Добавляем отступ между текстом и прогресс-баром
            stackPanel.Children.Add(new TextBlock { Height = 10 }); // пустой элемент для отступа
            stackPanel.Children.Add(_progressBar);

            border.Child = stackPanel;
            grid.Children.Add(border);

            var progressAnimation = new DoubleAnimation(0, totalDurationMs, TimeSpan.FromMilliseconds(totalDurationMs));
            _progressBar.BeginAnimation(ProgressBar.ValueProperty, progressAnimation);
        }

        private void SetupSimpleOverlay(Grid grid, string message, string customBackground)
        {
            var bgColor = customBackground != null
                ? (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(customBackground))
                : new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));

            var border = new Border
            {
                Background = bgColor,
                Padding = new Thickness(20),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            grid.Children.Add(border);
        }

        private void SetupHelpOverlay(Grid grid, string message)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
                Padding = new Thickness(20),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.LightGray,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };

            grid.Children.Add(border);
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _closeTimer.Stop();
            StartFadeOut();
        }

        private void StartFadeOut()
        {
            if (_isFading) return;
            _isFading = true;

            var fadeAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(300)
            };

            fadeAnimation.Completed += (s, e) => Close();
            BeginAnimation(Window.OpacityProperty, fadeAnimation);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closeTimer?.Stop();
            BeginAnimation(Window.OpacityProperty, null);
            base.OnClosed(e);
        }
    }
}