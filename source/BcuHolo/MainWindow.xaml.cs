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
        // Windows 11 rounds the window in the compositor; Windows 10 needs a window region (see ApplyRoundRegion).
        private static readonly bool DwmCorners = Environment.OSVersion.Version.Build >= 22000;
        private static readonly double Radius = DwmCorners ? 8 : 16; // 8 = DWM's own corner radius, so the frame line follows the clip

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
            FitMaximized(); // applies Radius to the frame
            _rail = _modules.OrderBy(m => m.Order).Select(m => new RailItem(m)).ToList();
            ModuleList.ItemsSource = _rail;
            // The widget reads the GPU engine even when this window is never shown (--widget at logon).
            _modules.OfType<BcuHolo.Modules.Gpu.GpuModule>().First().Start(this);
            // Build the first page only once the window is shown: the dashboard tiles start disk walks, WMI and COM scans.
            DependencyPropertyChangedEventHandler firstShow = null;
            firstShow = (_, _) =>
            {
                if (!IsVisible) return;
                IsVisibleChanged -= firstShow;
                ModuleList.SelectedIndex = 0;
                // "--page cleanup" opens straight on a module (handy for shortcuts and screenshots).
                var args = Environment.GetCommandLineArgs();
                var i = Array.IndexOf(args, "--page");
                if (i > 0 && i + 1 < args.Length) Navigate(args[i + 1]);
            };
            IsVisibleChanged += firstShow;

            StateChanged += (_, _) => { FitMaximized(); ApplyRoundRegion(); };
            // A window region repaints the whole window on every resize step; only Windows 10 needs it.
            if (!DwmCorners) SizeChanged += (_, _) => ApplyRoundRegion();
            SourceInitialized += (_, _) =>
            {
                if (DwmCorners)
                {
                    int round = 2; // DWMWCP_ROUND
                    DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
                }
                ApplyRoundRegion();
                // Added after WindowChrome's own hook, so it runs first (HwndSource calls newest hooks first).
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                src.AddHook(CornerHitTest);
                src.AddHook(HoldPageDuringDrag);
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
        private long _themeClosedAt;
        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            if (_themePopup == null)
            {
                _themePopup = BcuHolo.Themes.ThemePicker.Create(ThemeButton);
                _themePopup.Closed += (_, _) => _themeClosedAt = Environment.TickCount64;
            }
            // StaysOpen=false already closed it on mouse-down; without this the click would reopen it at once.
            if (Environment.TickCount64 - _themeClosedAt < 300) return;
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

        /// <summary>
        /// Edge-drag resize: hold the page at its current size until the drag ends, so each resize step only re-lays out
        /// the frame and rail; the page re-flows once on release. (Pages cost 30-80 ms per re-layout; at 60 steps a
        /// second that was the choppiness.) Also fires for title-bar moves, where the size doesn't change anyway.
        /// </summary>
        private IntPtr HoldPageDuringDrag(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
            if (msg == WM_ENTERSIZEMOVE && ModuleHost.ActualWidth > 0)
            {
                ModuleHost.Width = ModuleHost.ActualWidth;
                ModuleHost.Height = ModuleHost.ActualHeight;
                ModuleHost.HorizontalAlignment = HorizontalAlignment.Left; // anchored, clipped when the window shrinks
                ModuleHost.VerticalAlignment = VerticalAlignment.Top;
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                ModuleHost.ClearValue(WidthProperty);
                ModuleHost.ClearValue(HeightProperty);
                ModuleHost.ClearValue(HorizontalAlignmentProperty);
                ModuleHost.ClearValue(VerticalAlignmentProperty);
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Windows 10 can't round top-level windows, so clip to a rounded region. The edge is aliased
        /// (regions are 1-bit); the glass border drawn just inside it hides most of that.
        /// Upgrade path on Windows 11: DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) instead.
        /// </summary>
        private void ApplyRoundRegion()
        {
            if (DwmCorners) return;
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
