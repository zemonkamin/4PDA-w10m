using System;
using System.Threading.Tasks;
using Windows.Web.Http;

namespace _4PDA.Services
{
    internal sealed class FourPdaChallengeException : InvalidOperationException
    {
        public FourPdaChallengeException()
            : base("4PDA требует проверку Cloudflare. Пока проверка не пройдена, сайт не отдаёт содержимое приложению.")
        {
        }
    }

    internal static class FourPdaResponseHelper
    {
        public static async Task<string> ReadHtmlAsync(HttpResponseMessage response)
        {
            string html = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, html);
            return html;
        }

        public static void EnsureSuccess(HttpResponseMessage response, string html = null)
        {
            string mitigation;
            bool challengeHeader = response.Headers.TryGetValue("cf-mitigated", out mitigation)
                && string.Equals(mitigation, "challenge", StringComparison.OrdinalIgnoreCase);
            if (challengeHeader || IsChallengePage(html))
                throw new FourPdaChallengeException();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("4PDA: HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);
        }

        internal static bool IsChallengePage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            return html.IndexOf("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase) >= 0
                && (html.IndexOf("_cf_chl_opt", StringComparison.OrdinalIgnoreCase) >= 0
                    || html.IndexOf("challenge-error-text", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
