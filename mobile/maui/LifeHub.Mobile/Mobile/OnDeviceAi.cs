using LifeHub.Integrations.Ollama;
using Microsoft.JSInterop;

namespace LifeHub.Mobile.Mobile;

/// <summary>
/// The AI that runs inside the app (WebLLM on the phone's GPU, in the app's web view — the same engine the Tolle app
/// uses). It works while the app is open; emails that arrive while it's closed are read the next time it opens.
/// </summary>
public sealed class OnDeviceAi : IOnDeviceAi
{
    private IJSRuntime? _js;
    private bool _gpu;

    public static readonly (string Id, string Note)[] Catalog =
    {
        ("Qwen3-1.7B-q4f16_1-MLC", "About 1.2 GB — quick, fine for most phones"),
        ("Qwen3-4B-q4f16_1-MLC", "About 2.5 GB — better answers, needs a recent phone"),
        ("Llama-3.2-3B-Instruct-q4f16_1-MLC", "About 1.8 GB"),
        ("gemma-2-2b-it-q4f16_1-MLC", "About 1.4 GB"),
    };

    /// <summary>Called by the app's main page once its web view is ready.</summary>
    public async Task AttachAsync(IJSRuntime js)
    {
        _js = js;
        try { _gpu = await js.InvokeAsync<bool>("lifehubOnDevice.supported"); } catch { _gpu = false; }
    }

    public bool Available => _js is not null && _gpu;
    public bool GpuSupported => _gpu;
    public IReadOnlyList<string> Models => Catalog.Select(c => c.Id).ToList();

    /// <summary>Downloads (once) and loads a model, reporting progress text.</summary>
    public async Task LoadAsync(string model, Action<string> progress, CancellationToken ct = default)
    {
        if (_js is null) throw new InvalidOperationException("Open LifeHub first.");
        using var callback = DotNetObjectReference.Create(new Progress(progress));
        await _js.InvokeVoidAsync("lifehubOnDevice.load", ct, model, callback);
    }

    public async Task<string> ChatAsync(string model, string system, string user, bool json, CancellationToken ct)
    {
        if (_js is null || !_gpu) throw new InvalidOperationException("The on-device AI isn't available on this phone right now.");
        return await _js.InvokeAsync<string>("lifehubOnDevice.chat", ct, model, system, user, json);
    }

    private sealed class Progress(Action<string> report)
    {
        [JSInvokable] public void Report(string text) => report(text);
    }
}
