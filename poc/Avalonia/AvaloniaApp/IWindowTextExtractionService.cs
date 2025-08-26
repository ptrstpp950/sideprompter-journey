using System.Threading.Tasks;

namespace AvaloniaApp
{
    public interface IWindowTextExtractionService
    {
        /// <summary>
        /// Gets the text from the current active window in the system
        /// </summary>
        /// <returns>The text content of the active window, or empty string if no text could be extracted</returns>
        Task<string> GetActiveWindowTextAsync();
        
        /// <summary>
        /// Gets the title of the current active window in the system
        /// </summary>
        /// <returns>The title of the active window</returns>
        string GetActiveWindowTitle();
    }
}
