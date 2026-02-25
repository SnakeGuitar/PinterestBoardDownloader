using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using PinterestBoardDownloader.Model;
using System.IO;
using System.Net.Http;

namespace PinterestBoardDownloader.Services
{
    public class PinterestScraper
    {
        private readonly HttpClient _httpClient;

        public PinterestScraper()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<List<string>> ScrapeUrlsAsync(string boardUrl, int maxPins, DownloadMode mode, CancellationToken token, Action<string> statusCallback)
        {
            var validUrls = new HashSet<string>();
            IWebDriver? driver = null;

            try
            {
                var service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                var options = new ChromeOptions();
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--start-maximized");

                driver = new ChromeDriver(service, options);
                driver.Navigate().GoToUrl(boardUrl);

                statusCallback("🌐 Accessing Pinterest...");
                await Task.Delay(5000, token);

                IJavaScriptExecutor js = (IJavaScriptExecutor)driver;

                long lastScrollY = -1;
                int noChangeCount = 0;
                bool boardEndReached = false;

                while (validUrls.Count < maxPins && !boardEndReached)
                {
                    token.ThrowIfCancellationRequested();

                    var imgElements = driver.FindElements(By.TagName("img"));

                    if (mode == DownloadMode.Strict_BoardOnly)
                    {
                        var headers = driver.FindElements(By.XPath("//*[contains(text(), 'More like this') or contains(text(), 'More ideas') or contains(text(), 'Más como esto')]"));
                        if (headers.Count > 0 && headers[0].Displayed)
                        {
                            boardEndReached = true;
                            statusCallback("🛑 End of board detected. Stopping search.");
                            break;
                        }
                    }

                    foreach (var img in imgElements)
                    {
                        try
                        {
                            string src = img.GetAttribute("src");
                            if (IsValidPinUrl(src))
                            {
                                string highRes = GetHighResUrl(src);
                                validUrls.Add(highRes);
                            }
                        }
                        catch {}
                    }

                    statusCallback($"🔍 Found {validUrls.Count} pins...");

                    if (validUrls.Count >= maxPins) break;

                    js.ExecuteScript("window.scrollBy(0, window.innerHeight);");

                    await Task.Delay(1500, token);

                    long currentScrollY = (long)js.ExecuteScript("return window.scrollY");

                    if (currentScrollY == lastScrollY)
                    {
                        noChangeCount++;
                        if (noChangeCount >= 4) break;
                    }
                    else
                    {
                        noChangeCount = 0;
                        lastScrollY = currentScrollY;
                    }
                }
            }
            finally
            {
                driver?.Quit();
                driver?.Dispose();
            }

            return validUrls.Take(maxPins).ToList();
        }

        public async Task DownloadImageAsync(string url, string folderPath, int index, CancellationToken token)
        {
            string fileName = $"Pin_{index:000}.jpg";
            string fullPath = Path.Combine(folderPath, fileName);

            if (File.Exists(fullPath)) return;

            var data = await _httpClient.GetByteArrayAsync(url, token);
            await File.WriteAllBytesAsync(fullPath, data, token);
        }

        private bool IsValidPinUrl(string src)
        {
            return !string.IsNullOrEmpty(src) &&
                   src.Contains("i.pinimg.com") &&
                   !src.Contains("75x75") &&
                   !src.Contains("profile_display");
        }

        private string GetHighResUrl(string src)
        {
            return src.Replace("/236x/", "/originals/")
                      .Replace("/474x/", "/originals/")
                      .Replace("/564x/", "/originals/");
        }
    }
}