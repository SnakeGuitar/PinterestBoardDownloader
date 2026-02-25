using PinterestBoardDownloader.Model;
using PinterestBoardDownloader.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace PinterestBoardDownloader
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly PinterestScraper _scraper;
        private CancellationTokenSource? _cts;
        private bool _isPaused = false;
        private bool _isRunning = false;

        public ObservableCollection<PinItem> ProcessedFiles { get; set; } = new();

        public string BoardUrl { get; set; } = "https://www.pinterest.com/usuario/tablero";

        private int _maxPins = 100;
        public int MaxPins
        {
            get => _maxPins;
            set { _maxPins = value; OnProp("MaxPins"); }
        }

        private string? _savePath;
        public string SavePath
        {
            get => _savePath;
            set { _savePath = value; OnProp("SavePath"); }
        }

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { _status = value; OnProp("Status"); }
        }

        private DownloadMode _selectedMode = DownloadMode.Fast_Limit;
        public DownloadMode SelectedMode
        {
            get => _selectedMode;
            set { _selectedMode = value; OnProp("SelectedMode"); }
        }

        public MainWindowViewModel()
        {
            _scraper = new PinterestScraper();
        }

        public async Task DownloadAsync()
        {
            if (string.IsNullOrEmpty(SavePath))
            {
                Status = "❌ Error: Select a folder first.";
                return;
            }

            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _cts = new CancellationTokenSource();
            ProcessedFiles.Clear();

            Status = $"🚀 Starting ({SelectedMode})...";

            try
            {
                var urls = await Task.Run(() =>
                    _scraper.ScrapeUrlsAsync(BoardUrl, MaxPins, SelectedMode, _cts.Token, (msg) => Status = msg)
                );

                if (urls.Count == 0)
                {
                    Status = "⚠️ No images found.";
                    return;
                }

                Status = $"✅ Found {urls.Count} images. Starting download...";

                int counter = 1;
                foreach (string url in urls)
                {
                    await CheckControlFlags();

                    try
                    {
                        await _scraper.DownloadImageAsync(url, SavePath, counter, _cts.Token);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ProcessedFiles.Add(new PinItem { Url = url, FileName = $"Pin_{counter:000}.jpg" });
                        });

                        Status = $"⬇ Downloading {counter}/{urls.Count}...";
                        counter++;

                        if (counter % 50 == 0) await Task.Delay(2000, _cts.Token);
                        else await Task.Delay(Random.Shared.Next(100, 300), _cts.Token);

                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }
                }

                Status = "✨ Process Completed Successfully!";
            }
            catch (OperationCanceledException)
            {
                Status = "🛑 Stopped by user.";
            }
            catch (Exception ex)
            {
                Status = $"☠ Error: {ex.Message}";
            }
            finally
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public void RequestPause()
        {
            if (_isRunning && !_isPaused)
            {
                _isPaused = true;
                Status = "⏸ Paused. Press 'Continue' to resume.";
            }
        }

        public void RequestResume()
        {
            if (_isRunning && _isPaused)
            {
                _isPaused = false;
                Status = "▶ Resuming...";
            }
        }

        public void RequestStop()
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                Status = "🛑 Stopping...";
            }
        }

        private async Task CheckControlFlags()
        {
            _cts?.Token.ThrowIfCancellationRequested();
            while (_isPaused)
            {
                await Task.Delay(500);
                _cts?.Token.ThrowIfCancellationRequested();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}