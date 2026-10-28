using System.Net;
using _4PDA.Models;

namespace _4PDA.Services
{
    internal static class OfficialApiFavorites
    {
        internal static ForumTopicItem Item(ApiArray row)
        {
            // Page_Favorites.e0: 0 = forum, 1 = topic. Field 11 is notification mode.
            int kind = row.Int(0), id = row.Int(1), flags = row.Int(4);
            if (row.Count < 3 || (kind != 0 && kind != 1) || id <= 0) return null;
            return new ForumTopicItem
            {
                Id = id, ForumId = kind == 0 ? id : 0, Title = WebUtility.HtmlDecode(row.Text(2)),
                IsForum = kind == 0, IsNew = (flags & 32) != 0, IsClosed = kind == 1 && (flags & 4) != 0,
                LastUser = kind == 1 ? WebUtility.HtmlDecode(row.Text(8)) : string.Empty,
                Url = "https://4pda.to/forum/index.php?" + (kind == 0 ? "showforum=" : "showtopic=") + id
            };
        }
    }
}
