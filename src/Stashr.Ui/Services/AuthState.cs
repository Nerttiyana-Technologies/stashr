using Microsoft.JSInterop;

namespace Stashr.Ui.Services;

/// <summary>Holds the active client token (in memory + sessionStorage for refresh survival).</summary>
public sealed class AuthState
{
    private readonly IJSRuntime _js;

    public AuthState(IJSRuntime js) => _js = js;

    public string? Token { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public event Action? Changed;

    public async Task InitAsync()
    {
        Token = await _js.InvokeAsync<string?>("stashrSession.get");
        Changed?.Invoke();
    }

    public async Task SignInAsync(string token)
    {
        Token = token;
        await _js.InvokeVoidAsync("stashrSession.set", token);
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        Token = null;
        await _js.InvokeVoidAsync("stashrSession.clear");
        Changed?.Invoke();
    }
}
