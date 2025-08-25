using System.Threading;
using System.Threading.Tasks;

namespace SidePrompterMauiApp.Services
{
    public interface IWindowProtectionService
    {
        Task<(bool Success, string Message)> SetWindowProtectionAsync(bool enable, CancellationToken ct = default);
    }
}