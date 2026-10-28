using Windows.UI.Xaml;

namespace _4PDA.Models
{
    public sealed class ForumTopicItem
    {
        public int Id { get; set; }
        public int ForumId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string LastUser { get; set; }
        public string Date { get; set; }
        public string Curator { get; set; }
        public string Url { get; set; }
        public bool IsForum { get; set; }
        public bool IsPinned { get; set; }
        public bool IsClosed { get; set; }
        public bool IsPoll { get; set; }
        public bool IsNew { get; set; }

        public string KindText
        {
            get
            {
                if (IsForum)
                {
                    return "Форум";
                }

                if (IsPinned)
                {
                    return "Важная тема";
                }

                return "Тема";
            }
        }

        public string MarkerGlyph
        {
            get
            {
                if (IsForum)
                {
                    return "\uE8B7";
                }

                if (IsClosed)
                {
                    return "\uE72E";
                }

                if (IsPoll)
                {
                    return "\uE9D2";
                }

                if (IsPinned)
                {
                    return "\uE718";
                }

                return "\uE8A5";
            }
        }

        public string MetaText
        {
            get
            {
                string author = string.IsNullOrWhiteSpace(Author) ? "" : "автор: " + Author;
                string last = string.IsNullOrWhiteSpace(LastUser) ? "" : "послед.: " + LastUser;
                string date = string.IsNullOrWhiteSpace(Date) ? "" : Date;

                if (!string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(date))
                {
                    last += " " + date;
                }
                else if (string.IsNullOrWhiteSpace(last))
                {
                    last = date;
                }

                if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(last))
                {
                    return author + "   " + last;
                }

                return author + last;
            }
        }

        public string ExtraText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Curator))
                {
                    return "куратор: " + Curator;
                }

                return string.Empty;
            }
        }

        public Visibility DescriptionVisibility
        {
            get { return string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ExtraVisibility
        {
            get { return string.IsNullOrWhiteSpace(ExtraText) ? Visibility.Collapsed : Visibility.Visible; }
        }
    }
}
