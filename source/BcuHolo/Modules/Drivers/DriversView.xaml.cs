using System.Windows;
using System.Windows.Controls;

namespace BcuHolo.Modules.Drivers
{
    public partial class DriversView : UserControl
    {
        private readonly DriversModel _model;

        public DriversView(DriversModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            // The shell caches this view; the model guards the scan so it runs once.
            Loaded += (_, _) => _ = model.LoadLocalAsync();
        }

        private void Check_Click(object sender, RoutedEventArgs e) => _ = _model.CheckUpdatesAsync();

        private void Filter_Click(object sender, RoutedEventArgs e) =>
            _model.ShowMicrosoft = (string)((RadioButton)sender).Tag == "all";

        private void Nvidia_Click(object sender, RoutedEventArgs e) => _model.Open("https://www.nvidia.com/en-us/drivers/");

        private void OpenWu_Click(object sender, RoutedEventArgs e) => _model.Open("ms-settings:windowsupdate-optionalupdates");
    }
}
