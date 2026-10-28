using System;

namespace _4PDA.Services
{
    internal sealed class TopicPagingState
    {
        internal int PageSize { get; private set; }
        internal int CurrentPage { get; private set; }
        internal int TotalPages { get; private set; }
        internal bool IsLoaded { get; private set; }
        internal bool CanPrevious { get { return CurrentPage > 1; } }
        internal bool CanNext { get { return CurrentPage < TotalPages; } }

        internal TopicPagingState() { Reset(); }

        internal void Reset()
        {
            PageSize = 20; CurrentPage = TotalPages = 1; IsLoaded = false;
        }

        internal void Apply(int start, int totalPages, int pageSize)
        {
            PageSize = Math.Max(1, pageSize);
            TotalPages = (int)Math.Max(1L, Math.Min(totalPages, (long)int.MaxValue / PageSize + 1));
            CurrentPage = (int)Math.Max(1L, Math.Min((long)Math.Max(0, start) / PageSize + 1, TotalPages));
            IsLoaded = true;
        }

        internal bool ShowPagination(bool usePages, bool loading, int loadedPosts)
        {
            return IsLoaded && !loading && loadedPosts > 0 && (usePages || !CanNext);
        }

        internal int OffsetForPage(int page)
        {
            return (Math.Max(1, Math.Min(page, TotalPages)) - 1) * PageSize;
        }
    }
}
