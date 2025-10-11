using Nunu_The_AI_Companion;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NunuTheAICompanion.Services;

public sealed class ImageClient
{
    private readonly HttpClient _http;
    private readonly Configuration _cfg;
    private readonly string _saveBase;

    public ImageClient(HttpClient http, Configuration cfg, string saveBase)
    {
        _http = http; _cfg = cfg; _saveBase = saveBase;
        Directory.CreateDirectory(_saveBase);
    }

    public async Task<string> Txt2ImgAsync(string prompt, CancellationToken token)
    {
        // Minimal stub that writes a placeholder file; replace with real A1111 call later
        var file = Path.Combine(_saveBase, $"nunu_{System.DateTimeOffset.Now.ToUnixTimeSeconds()}.txt");
        await File.WriteAllTextAsync(file, $"prompt: {prompt}", token).ConfigureAwait(false);
        return file;
    }
}
