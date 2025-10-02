using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading.Tasks;
using Windows.Graphics;
using WinUIEx;
using CubeKit.UI.Helpers;

namespace Clippy
{
    public class TrayFlyoutWindow : WindowEx
    {
        private readonly WindowIconManager _trayIconManager;
        private readonly Window _mainWindow;
        private bool _isShown = false;

        public TrayFlyoutWindow(Window mainWindow)
        {
            _mainWindow = mainWindow;

            // Set window properties
            this.Title = "Clippy";
            this.SetWindowSize(400, 500);
            this.Content = CreateFlyoutContent();
            this.ExtendsContentIntoTitleBar = true;
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Initialize tray icon
            _trayIconManager = new WindowIconManager(this);
            //_trayIconManager.Icon = default;//new Windows.Storage.Streams.InMemoryRandomAccessStream();
            _trayIconManager.Tooltip = "Clippy";
            _trayIconManager.Show();

            // Handle tray icon click
            _trayIconManager.TrayIconClicked += (s, e) => ToggleWindow();

            // Set window position
            SetWindowPosition();

            // Handle window events
            this.Closed += (s, e) => _trayIconManager.Dispose();

            // Hide window initially
            this.Hide();
        }

        private UIElement CreateFlyoutContent()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

            // Header with title and close button
            var header = new Grid()
            {
                Height = 40,
                Background = new SolidColorBrush(Colors.Transparent),
                // Padding = new Thickness(12, 8),
                ColumnDefinitions =
                {
                    new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition() { Width = GridLength.Auto }
                }
            };

            var title = new TextBlock()
            {
                Text = "Clippy",
                VerticalAlignment = VerticalAlignment.Center,
                //FontWeight = Windows.UI.Text.FontWeights.SemiBold
            };
            Grid.SetColumn(title, 0);

            var closeButton = new Button()
            {
                Content = "×",
                Width = 32,
                Height = 32,
                FontSize = 20,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeButton.Click += (s, e) => this.Hide();
            Grid.SetColumn(closeButton, 1);

            header.Children.Add(title);
            header.Children.Add(closeButton);

            // Content
            var scrollViewer = new ScrollViewer()
            {
                Padding = new Thickness(12)
            };

            var stackPanel = new StackPanel()
            {
                Spacing = 8
            };

            // Add your flyout content here
            stackPanel.Children.Add(new TextBlock() { Text = "Clippy is running in the background." });
            stackPanel.Children.Add(new Button()
            {
                Content = "Show Clippy",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0)
            }.Apply(btn => btn.Click += (s, e) =>
            {
                _mainWindow.Show();
                //_mainWindow.BringToFront();
                this.Hide();
            }));

            stackPanel.Children.Add(new Button()
            {
                Content = "Settings",
                HorizontalAlignment = HorizontalAlignment.Stretch
            }.Apply(btn => btn.Click += (s, e) =>
            {
                // Open settings
                var settingsWindow = new SettingsWindow();
                settingsWindow.Activate();
            }));

            stackPanel.Children.Add(new Button()
            {
                Content = "Exit",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8)
            }.Apply(btn => btn.Click += (s, e) =>
            {
                App.Current.Exit();
            }));

            scrollViewer.Content = stackPanel;

            Grid.SetRow(header, 0);
            Grid.SetRow(scrollViewer, 1);

            grid.Children.Add(header);
            grid.Children.Add(scrollViewer);

            return grid;
        }

        private void SetWindowPosition()
        {
            // Get taskbar position and size
            var displayArea = DisplayArea.GetFromWindowId(
                Win32Interop.GetWindowIdFromWindow(this.GetWindowHandle()),
                DisplayAreaFallback.Primary);

            var workArea = displayArea.WorkArea;
            var dpiScale = this.GetDpiForWindow() / 96.0;

            // Position window above taskbar
            this.MoveWindow(
                (DisplayArea.Primary.OuterBounds.Width) - (int)(this.Width * dpiScale) - 10,
                (DisplayArea.Primary.OuterBounds.Height) - (int)(this.Height * dpiScale) - 10,
                this.Width,
                this.Height);
        }

        private void MoveWindow(double x, double y, double width, double height)
        {
            // Move window
            this.Move((int)x, (int)y);
        }

        public void ToggleWindow()
        {
            if (_isShown)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                this.BringToFront();
                this.Activate();
            }
            _isShown = !_isShown;
        }
    }

    public static class Extensions
    {
        public static T Apply<T>(this T element, Action<T> action) where T : UIElement
        {
            action(element);
            return element;
        }
    }
}