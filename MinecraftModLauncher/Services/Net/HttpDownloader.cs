using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services.Net;

public class HttpDownloader
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore;

    public HttpDownloader(HttpClient httpClient, int maxConcurrentDownloads = 10)
    {
        _httpClient = httpClient;
        _semaphore = new SemaphoreSlim(maxConcurrentDownloads);
    }

    public async Task DownloadFile(string url, string destPath)
    {
        if (File.Exists(destPath)) return;

        await _semaphore.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using HttpResponseMessage response =
                await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using FileStream fileStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            if (File.Exists(destPath)) File.Delete(destPath);

            throw new Exception($"Failed to download {url}: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}