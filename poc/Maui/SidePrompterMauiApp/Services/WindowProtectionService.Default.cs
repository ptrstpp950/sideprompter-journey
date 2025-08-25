
using System.Threading;
using System.Threading.Tasks;

namespace SidePrompterMauiApp.Services
{


    public partial class WindowProtectionService : IWindowProtectionService
    {

#if !(WINDOWS || MACCATALYST)
        public Task<(bool Success, string Message)> SetWindowProtectionAsync(bool enable, CancellationToken ct = default)
            => Task.FromResult((false, "Not supported on this platform"));
#endif
    }
}
