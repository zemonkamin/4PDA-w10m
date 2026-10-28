using System;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace _4PDA.Models
{
    public sealed class NewsItem : INotifyPropertyChanged
    {
        private string _imageUrl;
        private string _html;
        private int _commentsCount;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id { get; set; }
        public int AuthorId { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Date { get; set; }

        public string ImageUrl
        {
            get { return _imageUrl; }
            set
            {
                if (_imageUrl == value)
                {
                    return;
                }

                _imageUrl = value;
                RaisePropertyChanged("ImageUrl");
                RaisePropertyChanged("ImageVisibility");
            }
        }

        public string Html
        {
            get { return _html; }
            set
            {
                if (_html == value)
                {
                    return;
                }

                _html = value;
                RaisePropertyChanged("Html");
            }
        }

        public int CommentsCount
        {
            get { return _commentsCount; }
            set
            {
                if (_commentsCount == value)
                {
                    return;
                }

                _commentsCount = value;
                RaisePropertyChanged("CommentsCount");
                RaisePropertyChanged("CommentsText");
            }
        }

        public string CommentsText
        {
            get { return CommentsCount > 0 ? CommentsCount.ToString() : string.Empty; }
        }

        public Visibility ImageVisibility
        {
            get { return string.IsNullOrWhiteSpace(ImageUrl) ? Visibility.Collapsed : Visibility.Visible; }
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
