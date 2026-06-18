using Microsoft.JSInterop;

namespace Stashr.Ui.Services;

/// <summary>Reads/writes the light|dark theme, persisted in localStorage via JS interop.</summary>
public sealed class ThemeService
{
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js) => _js = js;

    public string Current { get; private set; } = "light";
    public bool IsDark => Current == "dark";

    public event Action? Changed;

    public async Task InitAsync()
    {
        Current = await _js.InvokeAsync<string>("stashrTheme.init");
        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        Current = IsDark ? "light" : "dark";
        await _js.InvokeVoidAsync("stashrTheme.set", Current);
        Changed?.Invoke();
    }
}
