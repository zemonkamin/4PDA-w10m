namespace _4PDA.Models
{
    // The frame keeps this object on its back stack. Page changes update the URL
    // so returning from a linked post, settings or the editor restores that page.
    public sealed class TopicNavigationState
    {
        public TopicNavigationState(ForumTopicItem topic) { Topic = topic; Url = topic.Url; }
        public TopicNavigationState(string url) { Url = url; }
        public ForumTopicItem Topic { get; private set; }
        public string Url { get; set; }
    }
}
