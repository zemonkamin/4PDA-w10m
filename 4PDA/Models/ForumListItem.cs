using System;
using System.Collections.Generic;
using Windows.UI.Xaml;

namespace _4PDA.Models
{
    public sealed class ForumListItem
    {
        public ForumItem Forum { get; set; }
        public int Depth { get; set; }
        public bool IsSection { get; set; }
        public string Title { get { return Forum == null ? string.Empty : Forum.Title; } }
        public string LastTopicTitle { get { return Forum == null ? string.Empty : Forum.LastTopicTitle; } }
        public string LastPostInfo { get { return Forum == null ? string.Empty : Forum.LastPostInfo; } }
        public double Indent { get { return 16 + Math.Min(3, Math.Max(0, Depth - 1)) * 12; } }
        public Visibility SectionVisibility { get { return IsSection ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ForumVisibility { get { return IsSection ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility LastTopicVisibility { get { return string.IsNullOrWhiteSpace(LastTopicTitle) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility LastPostVisibility { get { return string.IsNullOrWhiteSpace(LastPostInfo) ? Visibility.Collapsed : Visibility.Visible; } }

        internal static List<ForumListItem> AllForums(ForumItem root)
        {
            var result = new List<ForumListItem>();
            if (root != null) Append(result, root, 0);
            return result;
        }

        private static void Append(List<ForumListItem> result, ForumItem parent, int depth)
        {
            foreach (ForumItem forum in parent.Children)
            {
                result.Add(new ForumListItem { Forum = forum, Depth = depth, IsSection = depth == 0 && forum.HasChildren });
                Append(result, forum, depth + 1);
            }
        }
    }
}
