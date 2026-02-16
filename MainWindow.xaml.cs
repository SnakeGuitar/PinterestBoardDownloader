using System.Windows;
using Microsoft.Win32;

namespace PinterestBoardDownloader
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainWindowViewModel();
            DataContext = _vm;
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Seleccionar carpeta",
                Title = "Selecciona cualquier archivo dentro de la carpeta destino"
            };

            if (dlg.ShowDialog() == true)
            {
                string folderPath = System.IO.Path.GetDirectoryName(dlg.FileName);
                _vm.SavePath = folderPath;
            }
        }

        private async void OnStart(object sender, RoutedEventArgs e)
        {
            await _vm.DownloadAsync();
        }
        private void OnPause(object sender, RoutedEventArgs e)
        {
            _vm.RequestPause();
        }
        private void OnContinue(object sender, RoutedEventArgs e)
        {
            _vm.RequestResume();
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            _vm.RequestStop();
        }
    }
}