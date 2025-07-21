using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace XIVLauncher.Common.Util;

public class HttpClientDownloadWithProgress : IDisposable
{
    private readonly string downloadUrl;
    private readonly string destinationFilePath;

    private HttpClient httpClient;

    public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    public event ProgressChangedHandler ProgressChanged;

    public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath)
    {
        this.downloadUrl = downloadUrl;
        this.destinationFilePath = destinationFilePath;
    }

    public async Task Download(TimeSpan? timeout = null, bool isNuget = false)
    {
        timeout ??= TimeSpan.FromMinutes(10);
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        this.httpClient = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip }) { Timeout = timeout.Value };
        //this.httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36 Edg/130.0.0.0");
        this.httpClient.DefaultRequestHeaders.Add("User-Agent", PlatformHelpers.GetVersion());
        this.httpClient.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate, br");
        var request = new HttpRequestMessage(HttpMethod.Get, this.downloadUrl);
        if (isNuget)
        {
            // GET /artifactory/api/nuget/v3/nuget-remote/microsoft.windowsdesktop.app.runtime.win-x64/9.0.3/microsoft.windowsdesktop.app.runtime.win-x64.9.0.3.nupkg HTTP/1.1
            // X-NuGet-Session-Id: 39bb13e5-0167-45e0-8196-82d141b14fb8
            // user-agent: NuGet VS VSIX/6.14.0 (WINDOWS, Community/17.0)
            // X-NuGet-Client-Version: 6.14.0
            // Accept-Language: zh-CN
            // Host: repo.huaweicloud.com
            // Accept-Encoding: gzip, deflate
            request.Headers.Add("User-Agent", "NuGet VS VSIX/6.14.0 (WINDOWS, Community/17.0)");
            request.Headers.Add("X-NuGet-Client-Version", "6.14.0");
            request.Headers.Add("X-NuGet-Session-Id", Guid.NewGuid().ToString("D"));
        }
        else if (!downloadUrl.Contains(ServerAddress.MainAddress))
        {
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36 Edg/130.0.0.0");

        }
        using var response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await this.DownloadFileFromHttpResponseMessage(response).ConfigureAwait(false);
    }

    private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await this.ProcessContentStream(totalBytes, contentStream).ConfigureAwait(false);
    }

    private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
    {
        var totalBytesRead = 0L;
        var readCount = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        using var fileStream = new FileStream(this.destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        do
        {
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                isMoreToRead = false;
                this.TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                continue;
            }

            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);

            totalBytesRead += bytesRead;
            readCount += 1;

            if (readCount % 100 == 0)
                this.TriggerProgressChanged(totalDownloadSize, totalBytesRead);
        } while (isMoreToRead);
    }

    private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
    {
        if (this.ProgressChanged == null)
            return;

        double? progressPercentage = null;
        if (totalDownloadSize.HasValue)
            progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

        this.ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
    }

    public void Dispose()
    {
        this.httpClient?.Dispose();
    }
}
