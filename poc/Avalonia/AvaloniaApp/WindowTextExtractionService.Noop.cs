using System.Threading.Tasks;

namespace AvaloniaApp
{
    /// <summary>
    /// Fallback implementation used on unsupported platforms.
    /// </summary>
    public sealed class NoopWindowTextExtractionService : IWindowTextExtractionService
    {
        public Task<string> GetActiveWindowTextAsync() => Task.FromResult(string.Empty);
        public string GetActiveWindowTitle() => string.Empty;
    }
}
