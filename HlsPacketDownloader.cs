using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Media.Streaming.Adaptive;

namespace SpectraGrab.HLS
{
    public class HlsPacketDownloader
    {
        private readonly HttpClient _http = new HttpClient();
        private AdaptiveMediaSource _ams;
        private string _outputFolder;

        public async Task InitializeAsync(string m3u8Url)
        {
            _outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "SpectraGrab", "HLS");

            Directory.CreateDirectory(_outputFolder);

            var result = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(m3u8Url));
            if (result.Status != AdaptiveMediaSourceCreationStatus.Success)
                throw new Exception("Failed to load HLS manifest.");

            _ams = result.MediaSource;

            _ams.DownloadRequested += OnDownloadRequested;
            _ams.DownloadCompleted += OnDownloadCompleted;
            _ams.DownloadFailed += OnDownloadFailed;
        }

        private async void OnDownloadRequested(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadRequestedEventArgs args)
        {
            try
            {
                var segmentUri = args.ResourceUri;
                var bytes = await _http.GetByteArrayAsync(segmentUri);

                var fileName = $"segment_{DateTime.Now.Ticks}.ts";
                var path = Path.Combine(_outputFolder, fileName);

                await File.WriteAllBytesAsync(path, bytes);

                args.Result.Buffer = bytes.AsBuffer();
                args.Result.InputStream = null;
                args.Result.Status = AdaptiveMediaSourceResourceStatus.Success;
            }
            catch
            {
                args.Result.Status = AdaptiveMediaSourceResourceStatus.OtherError;
            }
        }

        private void OnDownloadCompleted(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadCompletedEventArgs args)
        {
            // Optional: UI progress updates
        }

        private void OnDownloadFailed(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadFailedEventArgs args)
        {
            // Optional: retry logic
        }

        public string GetOutputFolder() => _outputFolder;
    }
}
