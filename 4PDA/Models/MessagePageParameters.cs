namespace _4PDA.Models
{
    public sealed class MessagePageParameters
    {
        public int TopicId { get; set; }
        public int EditPostId { get; set; }
        public int[] ExistingAttachmentIds { get; set; }
        public int ForumId { get; set; }
        public int Start { get; set; }
        public string TopicTitle { get; set; }
        public string TopicUrl { get; set; }
        public string AuthKey { get; set; }
        public bool CanReply { get; set; }
        public string InitialText { get; set; }

        public MessagePageParameters Clone()
        {
            return new MessagePageParameters
            {
                TopicId = TopicId,
                EditPostId = EditPostId,
                ExistingAttachmentIds = ExistingAttachmentIds == null ? null : (int[])ExistingAttachmentIds.Clone(),
                ForumId = ForumId,
                Start = Start,
                TopicTitle = TopicTitle,
                TopicUrl = TopicUrl,
                AuthKey = AuthKey,
                CanReply = CanReply,
                InitialText = InitialText
            };
        }
    }
}
