using System;
using System.Threading;
using System.Threading.Tasks;

namespace _4PDA.Services
{
    internal static class OfficialApiRequestRunner
    {
        private static readonly string[] ReadCommands = { "fi", "fs", "fr", "fj", "sl", "sa", "mi", "rc", "mf", "ql", "qt", "de", "fa", "le" };

        internal static bool IsRead(string command)
        {
            return Array.IndexOf(ReadCommands, command) >= 0;
        }

        internal static async Task<T> RunAsync<T>(string command, bool upload,
            Func<Task> prepare, Func<Task<T>> send, Action disconnect, Func<Task> refresh, CancellationToken cancellation)
        {
            for (int attempt = 0; ; attempt++)
            {
                bool dispatched = false;
                try
                {
                    await prepare();
                    cancellation.ThrowIfCancellationRequested();
                    dispatched = true;
                    return await send();
                }
                catch (OfficialApiException)
                {
                    // Business errors (including CAPTCHA) belong to the caller.
                    // Replaying them cannot repair the transport.
                    if (!dispatched) disconnect();
                    throw;
                }
                catch
                {
                    disconnect();
                    // Before dispatch, even login or an upload can safely wait
                    // for recovery. Once sent, only public/read requests repeat.
                    if (attempt > 0 || cancellation.IsCancellationRequested || (dispatched && (upload || !IsRead(command)))) throw;
                }
                await refresh();
            }
        }
    }
}
