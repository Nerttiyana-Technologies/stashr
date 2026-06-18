namespace Stashr.Ui.Services;

/// <summary>Cached instance-wide state (seal status) shared across components.</summary>
public sealed class AppState
{
    private readonly StashrApi _api;

    public AppState(StashrApi api) => _api = api;

    public SealStatusDto? Seal { get; private set; }
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }

    public event Action? Changed;

    public async Task RefreshSealAsync()
    {
        try
        {
            Seal = await _api.SealStatusAsync();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loaded = true;
            Changed?.Invoke();
        }
    }
}
