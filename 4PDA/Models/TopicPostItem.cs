using Windows.UI.Xaml;

namespace _4PDA.Models
{
    public sealed class TopicPostItem
    {
        public int Id { get; set; }
        public int TopicId { get; set; }
        public int ForumId { get; set; }
        public int Number { get; set; }
        public int UserId { get; set; }
        public string Author { get; set; }
        public string UserInfo { get; set; }
        public string Reputation { get; set; }
        public string Date { get; set; }
        public string AvatarUrl { get; set; }
        public string BodyHtml { get; set; }
        public string BodyBbCode { get; set; }
        public int[] AttachmentIds { get; set; }
        public bool IsOnline { get; set; }
        public bool CanMinus { get; set; }
        public bool CanPlus { get; set; }
        public bool CanReport { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanQuote { get; set; }
        public string EditUrl { get; set; }
        public string DeleteUrl { get; set; }

        public string NumberText
        {
            get { return Number > 0 ? "#" + Number.ToString() : string.Empty; }
        }

        public string ReputationText
        {
            get { return string.IsNullOrWhiteSpace(Reputation) ? string.Empty : "Реп: " + Reputation; }
        }

        public Visibility AvatarVisibility
        {
            get { return string.IsNullOrWhiteSpace(AvatarUrl) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility UserInfoVisibility
        {
            get { return string.IsNullOrWhiteSpace(UserInfo) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ReputationVisibility
        {
            get { return string.IsNullOrWhiteSpace(Reputation) ? Visibility.Collapsed : Visibility.Visible; }
        }
    }
}
