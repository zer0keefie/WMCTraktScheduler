
namespace TraktWmcScheduler.Entities
{
    public partial class WatchlistMovie
    {
        public WatchlistMovie() { }

        public WatchlistMovie(string title, int year, int traktId)
        {
            this.Title = title;
            this.Year = year;
            this.TraktId = traktId;
            this.Removed = false;
            this.Scheduled = false;
        }

        public override bool Equals(object obj)
        {
            WatchlistMovie other = obj as WatchlistMovie;
            if (other == null) return false;

            return this.Title == other.Title &&
                this.Year == other.Year &&
                this.TraktId == other.TraktId;
        }

        public override int GetHashCode()
        {
            return Title.GetHashCode();
        }

        public override string ToString()
        {
            return string.Concat(Title, " (", Year.ToString(), ")");
        }
    }
}
