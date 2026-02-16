using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace PinterestBoardDownloader
{
    public class PinItem
    {
        public required string Url { get; set; }
        public required string FileName { get; set; }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
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

        private string _status = "Listo";
        public string Status { get => _status; set { _status = value; OnProp("Status"); } }

        private CancellationTokenSource? _cts;
        private bool _isPaused = false;
        private bool _isRunning = false;

        private HttpClient _downloader = new HttpClient();

        public MainWindowViewModel() { }

        public void RequestPause()
        {
            if (_isRunning && !_isPaused)
            {
                _isPaused = true;
                UpdateStatus("⏸ Pausado. Pulsa 'Continuar' para seguir.");
            }
        }

        public void RequestResume()
        {
            if (_isRunning && _isPaused)
            {
                _isPaused = false;
                UpdateStatus("▶ Reanudando operaciones...");
            }
        }

        public void RequestStop()
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                UpdateStatus("🛑 Deteniendo proceso...");
            }
        }

        public async Task DownloadAsync()
        {
            if (string.IsNullOrEmpty(SavePath))
            {
                Status = "❌ Error: Selecciona una carpeta primero.";
                return;
            }

            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _cts = new CancellationTokenSource();

            Status = $"🚀 Iniciando (Meta: {MaxPins} pines)...";

            await Task.Run(async () =>
            {
                IWebDriver? driver = null;
                Random rnd = new Random();

                try
                {
                    var service = ChromeDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;

                    var options = new ChromeOptions();
                    options.AddArgument("--disable-blink-features=AutomationControlled");
                    options.AddArgument("--start-maximized");

                    driver = new ChromeDriver(service, options);

                    UpdateStatus("🌐 Entrando a Pinterest...");
                    driver.Navigate().GoToUrl(BoardUrl);

                    await CheckControlFlags();
                    await Task.Delay(rnd.Next(4000, 6000));

                    UpdateStatus("📜 Escaneando tablero...");
                    IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
                    var validUrls = new HashSet<string>();

                    long lastHeight = 0;
                    int noChangeCount = 0;

                    while (validUrls.Count < MaxPins)
                    {
                        await CheckControlFlags();

                        var imgElements = driver.FindElements(By.TagName("img"));
                        foreach (var img in imgElements)
                        {
                            try
                            {
                                string src = img.GetAttribute("src");
                                if (!string.IsNullOrEmpty(src) && src.Contains("i.pinimg.com") && !src.Contains("75x75"))
                                {
                                    string highRes = src.Replace("/236x/", "/originals/")
                                                        .Replace("/474x/", "/originals/")
                                                        .Replace("/564x/", "/originals/");
                                    validUrls.Add(highRes);
                                }
                            }
                            catch { }
                        }

                        UpdateStatus($"🔍 Recolectando... {validUrls.Count} / {MaxPins}");

                        if (validUrls.Count >= MaxPins) break;

                        long currentHeight = (long)js.ExecuteScript("return document.body.scrollHeight");
                        js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");

                        await Task.Delay(rnd.Next(2500, 4000));

                        long newHeight = (long)js.ExecuteScript("return document.body.scrollHeight");
                        if (newHeight == currentHeight)
                        {
                            noChangeCount++;
                            if (noChangeCount >= 4) break;
                            await Task.Delay(2000);
                        }
                        else
                        {
                            noChangeCount = 0;
                            lastHeight = newHeight;
                        }
                    }

                    var finalDetails = validUrls.Take(MaxPins).ToList();
                    UpdateStatus($"✅ Iniciando descarga de {finalDetails.Count} imágenes...");

                    int counter = 1;
                    _downloader.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    foreach (string url in finalDetails)
                    {
                        await CheckControlFlags();

                        try
                        {
                            string fileName = $"Pin_{counter:000}.jpg";
                            string fullPath = Path.Combine(SavePath, fileName);

                            if (!File.Exists(fullPath))
                            {
                                byte[] data = await _downloader.GetByteArrayAsync(url, _cts.Token);
                                await File.WriteAllBytesAsync(fullPath, data, _cts.Token);

                                await Task.Delay(rnd.Next(200, 600));

                                if (counter % 50 == 0)
                                {
                                    UpdateStatus($"☕ Pausa seguridad... ({counter}/{finalDetails.Count})");
                                    await Task.Delay(3000);
                                }
                            }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ProcessedFiles.Add(new PinItem { Url = url, FileName = fileName });
                            });

                            if (!_isPaused) UpdateStatus($"⬇ Descargando {counter}/{finalDetails.Count}...");
                            counter++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }

                    UpdateStatus("✨ ¡Proceso Completado Exitosamente!");
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("🛑 Proceso detenido por el usuario.");
                }
                catch (Exception ex)
                {
                    UpdateStatus("☠ Error: " + ex.Message);
                }
                finally
                {
                    _isRunning = false;
                    _isPaused = false;
                    if (driver != null) { driver.Quit(); driver.Dispose(); }
                    if (_cts != null) { _cts.Dispose(); _cts = null; }
                }
            });
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

        private void UpdateStatus(string msg) { _status = msg; OnProp("Status"); }
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}