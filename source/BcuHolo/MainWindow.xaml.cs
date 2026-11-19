using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BcuHolo.Modules.Apps;
using BcuHolo.Modules.Dashboard;
using BcuHolo.Plugins;

namespace BcuHolo
{
    /// <summary>Shell: rail of modules, caption buttons, rounded frame. Each module owns its own page.</summary>
    public partial class MainWindow : Window, IHoloHost
    {
        private const double Radius = 16;

        // Built-in modules. External plugins will be appended here by the loader (later phase).
        private readonly List<IHoloModule> _modules;

        private readonly Dictionary<string, FrameworkElement> _views = new();
        private List<RailItem> _rail;

        public MainWindow()
        {
            _modules = new List<IHoloModule>
            {
                new DashboardModule(() => _modules),
                new AppsModule(),
                new BcuHolo.Modules.Cleanup.CleanupModule(),
                new BcuHolo.Modules.Gpu.GpuModule(),
                new BcuHolo.Modules.Drivers.DriversModule(),
                new BcuHolo.Modules.Startup.StartupModule(),
                new BcuHolo.Modules.Sensors.SensorsModule(),
            };
            InitializeComponent();
            _rail = _modules.OrderBy(m => m.Order).Select(m => new RailItem(m)).ToList();
            ModuleList.ItemsSource = _rail;
            ModuleList.SelectedIndex = 0;
            // "--page cleanup" opens straight on a module (handy for shortcuts and screenshots).
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "--page");
            if (i > 0 && i + 1 < args.Length) Navigate(args[i + 1]);

            StateChanged += (_, _) => { FitMaximized(); ApplyRoundRegion(); };
            SizeChanged += (_, _) => ApplyRoundRegion();
            SourceInitialized += (_, _) =>
            {
                ApplyRoundRegion();
                // Added after WindowChrome's own hook, so it runs first (HwndSource calls newest hooks first).
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(CornerHitTest);
            };
        }

        private void Module_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ModuleList.SelectedItem is not RailItem item) return;
            if (!_views.TryGetValue(item.Module.Id, out var view))
                _views[item.Module.Id] = view = item.Module.CreateView(this);
            ModuleHost.Content = view;
            ModuleTitle.Text = item.Module.Title;
            Motion.Enter(view);
        }

        // ---- IHoloHost ----

        public void Navigate(string moduleId) =>
            ModuleList.SelectedItem = _rail.FirstOrDefault(r => r.Module.Id == moduleId) ?? ModuleList.SelectedItem;

        public bool Confirm(string title, string message) =>
            MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        public void SetBadge(string moduleId, string text)
        {
            var item = _rail.FirstOrDefault(r => r.Module.Id == moduleId);
            if (item != null) item.Badge = text;
        }

        // ---- Window chrome ----

        private System.Windows.Controls.Primitives.Popup _themePopup;
        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            _themePopup ??= BcuHolo.Themes.ThemePicker.Create(ThemeButton);
            // Opens beside whichever button was clicked: below the caption icon, to the right of the rail button.
            var anchor = (UIElement)sender;
            _themePopup.PlacementTarget = anchor;
            _themePopup.Placement = anchor == ThemeButton ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.Right;
            _themePopup.IsOpen = !_themePopup.IsOpen;
        }

        private void Widget_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ToggleWidget();

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Max_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // With WindowChrome a maximized window overhangs the screen by the resize border; pad it back in.
        private void FitMaximized()
        {
            var max = WindowState == WindowState.Maximized;
            var t = SystemParameters.WindowResizeBorderThickness;
            Frame.Margin = max ? new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4) : new Thickness(0);
            Frame.BorderThickness = new Thickness(max ? 0 : 1);
            Frame.CornerRadius = new CornerRadius(max ? 0 : Radius);
            MaxButton.Content = max ? "" : "";
        }

        private const double CornerGrip = 24;      // DIP; must exceed the region's corner inset (~0.3 * Radius) to stay clickable
        private const double CloseCornerGrip = 12; // DIP; top-right overlaps the Close button

        /// <summary>
        /// The rounded region clips away WindowChrome's 6px corner grips, leaving a sliver for diagonal resize.
        /// Claim a generous square at each corner (inside the curve) as a corner hit; edges stay with WindowChrome.
        /// </summary>
        private IntPtr CornerHitTest(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
            if (msg != WM_NCHITTEST || WindowState != WindowState.Normal || !GetWindowRect(hwnd, out var r)) return IntPtr.Zero;
            int x = unchecked((short)(long)lParam), y = unchecked((short)((long)lParam >> 16));
            var dpi = VisualTreeHelper.GetDpi(this);
            int g = (int)Math.Round(CornerGrip * dpi.DpiScaleX);
            bool left = x < r.Left + g, right = x >= r.Right - g, top = y < r.Top + g, bottom = y >= r.Bottom - g;
            // Top-right sits on the Close button: keep that grip small so Close stays easy to hit.
            int t = (int)Math.Round(CloseCornerGrip * dpi.DpiScaleX);
            bool topRight = x >= r.Right - t && y < r.Top + t;
            int hit = top && left ? HTTOPLEFT : topRight ? HTTOPRIGHT : bottom && left ? HTBOTTOMLEFT : bottom && right ? HTBOTTOMRIGHT : 0;
            if (hit == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(hit);
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        /// <summary>
        /// Windows 10 can't round top-level windows, so clip to a rounded region. The edge is aliased
        /// (regions are 1-bit); the glass border drawn just inside it hides most of that.
        /// Upgrade path on Windows 11: DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) instead.
        /// </summary>
        private void ApplyRoundRegion()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (WindowState == WindowState.Maximized)
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
            int d = (int)Math.Round(Radius * 2 * dpi.DpiScaleX);
            // The system owns the region after SetWindowRgn; don't delete it.
            SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d), true);
        }
    }

    /// <summary>Rail entry wrapper so badges can update live.</summary>
    public sealed class RailItem : INotifyPropertyChanged
    {
        private string _badge;
        public RailItem(IHoloModule m) => Module = m;
        public IHoloModule Module { get; }
        public string Title => Module.Title;
        public string Glyph => Module.Glyph;
        public string Badge
        {
            get => _badge;
            set { _badge = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Badge))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
