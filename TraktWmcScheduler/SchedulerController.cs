using Microsoft.MediaCenter.Guide;
using Microsoft.MediaCenter.Pvr;
using Microsoft.MediaCenter.Store;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using TraktSharp;
using TraktSharp.Entities;
using TraktWmcScheduler.Entities;
using TraktWmcScheduler.Properties;
using TraktSharp.Enums;
using TraktWmcScheduler.MediaCenter;
using Quartz;
using Quartz.Impl;

namespace TraktWmcScheduler
{
    /// <summary>
    /// Handles the bulk of the application logic.
    /// </summary>
    internal class SchedulerController : INotifyPropertyChanged, IDisposable
    {
        private const string EventLogName = "Media Center";
        private const string EventLogSource = "Trakt Scheduler";
        private bool eventLogEnabled = false;

        private IScheduler jobScheduler;
        private SchedulerDBEntities schedulerDb;
        private WMCDataSource wmcData;
        private DbSet<WatchlistMovie> localWatchlist;
        private IEnumerable<Program> epgMovies;

        private bool authenticationNeeded = true;

        public SchedulerController() { }

        /// <summary>
        /// Initialize connections to the local database and the Windows Media Center EPG database.
        /// </summary>
        public void Init()
        {
            schedulerDb = new SchedulerDBEntities();
            Client = new TraktClient();
            Client.Authentication.ClientId = TraktSettings.ClientId;
            Client.Authentication.ClientSecret = TraktSettings.ClientSecret;

            wmcData = new WMCDataSource();
            localWatchlist = schedulerDb.WatchlistMovies;
            localWatchlist.Load();

            jobScheduler = StdSchedulerFactory.GetDefaultScheduler();
            jobScheduler.Start();

            GetAuthInfo();

            RegisterEventSource();

            StartAutoUpdateTask();
        }

        private void StartAutoUpdateTask()
        {
            IJobDetail job = JobBuilder.Create<PeriodicUpdateJob>()
                .WithIdentity("TraktMonitor")
                .SetJobData(new JobDataMap {
                    { PeriodicUpdateJob.ControllerDataKey, this }
                })
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(30)
                    .RepeatForever())
                .Build();

            var next = jobScheduler.ScheduleJob(job, trigger);
            Log("Scheduled Auto-Update task. First run at {0}", next.ToLocalTime());
        }

        /// <summary>
        /// Runs an Auto-Update:
        /// - Check for any completed recordings
        /// - Download the latest watchlist from Trakt
        /// - Check the EPG for any matching programs and schedule them.
        /// </summary>
        /// <returns>True if the update completed successfully; false otherwise.</returns>
        public bool DoAutoUpdate()
        {
            if (AuthenticationNeeded)
            {
                return false;
            }

            FindCompletedRecordings();
            GetWatchlist();
            ScheduleForMovieWatchlist();

            return true;
        }

        #region Properties

        /// <summary>
        /// The Trakt client
        /// </summary>
        public TraktClient Client { get; private set; }

        /// <summary>
        /// Whether the user needs to authorize this application before accessing Trakt.
        /// </summary>
        public bool AuthenticationNeeded
        {
            get
            {
                return authenticationNeeded;
            }
            set
            {
                authenticationNeeded = value;
                OnPropertyChanged("AuthenticationNeeded");
                OnPropertyChanged("Authenticated");
            }
        }

        /// <summary>
        /// Whether the user has successfully authorized this application to access Trakt.
        /// </summary>
        /// <remarks>
        /// This property is the opposite of AuthenticationNeeded. It exists as a convenience for data binding.
        /// </remarks>
        public bool Authenticated
        {
            get { return !authenticationNeeded; }
        }

        /// <summary>
        /// The local copy of the user's Trakt Watchlist.
        /// </summary>
        public IEnumerable<WatchlistMovie> Watchlist
        {
            get { return localWatchlist.Local.OrderBy(x => x.Title).ThenBy(x => x.Year); }
        }

        /// <summary>
        /// The movies retreived from the EPG.
        /// </summary>
        public IEnumerable<Program> GuideMovies
        {
            get { return epgMovies; }
            set
            {
                epgMovies = value;
                OnPropertyChanged("GuideMovies");
            }
        }

        #endregion

        #region INotifyPropertyChanged Members

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler pch = PropertyChanged;
            if (pch != null)
            {
                pch.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            this.wmcData.Dispose();
            this.schedulerDb.Dispose();

            jobScheduler.Shutdown(true);
        }

        #endregion

        #region Logging

        private enum LogEventType
        {
            None = 0,
            RecordingScheduled = 1,
            RecordingCompleted = 2,
        }

        public delegate void LogMessageEventHandler(object sender, string message);

        public event LogMessageEventHandler LogMessage;

        /// <summary>
        /// Logs a simple message to the log listeners (and the debug console).
        /// </summary>
        /// <param name="message">The message to log.</param>
        private void Log(string message)
        {
            Debug.WriteLine(message);
            LogMessageEventHandler h = LogMessage;
            if (h != null)
            {
                h.Invoke(this, message);
            }
        }

        /// <summary>
        /// Logs a formatted message to the log listeners (and the debug console).
        /// </summary>
        /// <param name="format">The format string.</param>
        /// <param name="args">Arguments for the format string.</param>
        private void Log(string format, params object[] args)
        {
            Log(string.Format(format, args));
        }

        /// <summary>
        /// Ensures that the event source is created so we can log to the Event Log.
        /// </summary>
        private void RegisterEventSource()
        {
            try
            {
                if (!EventLog.SourceExists(EventLogSource))
                {
                    EventLog.CreateEventSource(EventLogSource, EventLogName);
                }
                eventLogEnabled = true;
            }
            catch (SecurityException)
            {
                Log("Disabled logging to the event log because the event source doesn't exist and we don't have permission to create it. Run this program once as an administrator to enable it.");
            }
        }

        /// <summary>
        /// Logs an event to the Event Log.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="entryType">The type of log entry.</param>
        /// <param name="eventId">The event id.</param>
        private void LogEvent(string message, EventLogEntryType entryType = EventLogEntryType.Information, LogEventType eventId = LogEventType.None)
        {
            if (eventLogEnabled)
            {
                EventLog.WriteEntry(EventLogSource, message, entryType, (int)eventId);
            }
            Log(message);
        }

        #endregion

        #region Authorization

        /// <summary>
        /// Retreives the saved OAuth access tokens.
        /// </summary>
        private async void GetAuthInfo()
        {
            OAuth auth = schedulerDb.OAuth.FirstOrDefault();
            if (auth == null) return;

            Client.Authentication.CurrentOAuthAccessToken = new TraktOAuthAccessToken
            {
                AccessToken = auth.AuthToken,
                RefreshToken = auth.RefreshToken,
                AccessTokenExpires = auth.Expires,
                AccessScope = auth.Scope,
            };

            AuthenticationNeeded = !Client.Authentication.CurrentOAuthAccessToken.IsValid;

            await RefreshToken();
        }

        /// <summary>
        /// Saves the OAuth access token.
        /// </summary>
        /// <param name="token">The OAuth access token returned from the server.</param>
        private void SaveAuthToken(TraktOAuthAccessToken token)
        {
            OAuth auth = schedulerDb.OAuth.FirstOrDefault();
            if (auth == null)
            {
                auth = new OAuth();
                schedulerDb.OAuth.Add(auth);
            }

            auth.AuthToken = token.AccessToken;
            auth.RefreshToken = token.RefreshToken;
            auth.Expires = token.AccessTokenExpires;
            auth.Scope = token.AccessScope;

            schedulerDb.SaveChanges();

            AuthenticationNeeded = !token.IsValid;
        }

        /// <summary>
        /// Presents the authentication form so the user can sign in to Trakt and approve the application.
        /// </summary>
        public async void Authenticate()
        {
            AuthWindow authForm = new AuthWindow(Client);
            bool? result = authForm.ShowDialog();

            if (result ?? false)
            {
                TraktOAuthAccessToken token = await Client.Authentication.GetOAuthAccessToken();
                if (token.IsValid)
                {
                    SaveAuthToken(token);
                    LogEvent(string.Format("Access to Trakt API granted. Access expires {0}.", token.AccessTokenExpires.ToLocalTime()));
                }
                else
                {
                    LogEvent(string.Format("Access to Trakt API denied. Access expired {0}.", token.AccessTokenExpires.ToLocalTime()), EventLogEntryType.Error);
                }
            }
        }

        /// <summary>
        /// If the current token has expired, requests a new one from Trakt.
        /// </summary>
        /// <returns>True if the user now has a valid token; false otherwise.</returns>
        public async Task<bool> RefreshToken()
        {
            var token = Client.Authentication.CurrentOAuthAccessToken;
            if (!string.IsNullOrEmpty(token.RefreshToken) && token.AccessTokenExpires < DateTime.UtcNow)
            {
                // Renew the token if it expired
                token = await Client.Authentication.RefreshOAuthAccessToken();
                if (token.IsValid)
                {
                    SaveAuthToken(token);
                    LogEvent(string.Format("Refreshed access token for Trakt API. Access expires {0}.", token.AccessTokenExpires.ToLocalTime()));
                }
                else
                {
                    LogEvent(string.Format("Unable to refresh access token for Trakt API. Access expired {0}.", token.AccessTokenExpires.ToLocalTime()), EventLogEntryType.Error);
                }
            }

            return token.IsValid;
        }

        #endregion

        #region EPG

        /// <summary>
        /// Determines whether a schedule entry actually occurs in the future.
        /// </summary>
        /// <param name="scheduleEntry">The EPG schedule entry.</param>
        /// <returns>True if the schedule entry is in the future; false otherwise.</returns>
        private static bool FutureAiring(ScheduleEntry scheduleEntry)
        {
            return scheduleEntry.StartTime >= DateTime.Now;
        }

        /// <summary>
        /// Searches the EPG for all movies scheduled in the next two weeks. The resulting list is available from the
        /// GuideMovies property.
        /// </summary>
        public void FindMovies()
        {
            var movies = wmcData.GetAllMovies(DateTime.Now, DateTime.Today.AddDays(14));
            GuideMovies = movies.OrderBy(movie => movie.Title).ThenBy(movie => movie.OriginalAirdate.Year);
        }

        #endregion

        #region Watchlist

        private static bool MoviesMatch(TraktMovie traktMovie, WatchlistMovie watchlistMovie)
        {
            // Try matching by ids first
            if (watchlistMovie.TraktId == traktMovie.Ids.Trakt) { return true; }

            // Fall back to title and year match (but only if we don't know the Trakt id)
            if (watchlistMovie.TraktId == 0 && watchlistMovie.Title == traktMovie.Title && watchlistMovie.Year == (traktMovie.Year ?? 0)) { return true; }

            return false;
        }

        /// <summary>
        /// Gets the latest Watchlist from Trakt.
        /// </summary>
        public async void GetWatchlist()
        {
            var traktWatchlist = (await Client.Sync.GetWatchlistMoviesAsync()).ToList();

            foreach (var item in traktWatchlist)
            {
                // TODO: Should we skip (or remove) items that are already collected?

                WatchlistMovie movie = localWatchlist.AsEnumerable().FirstOrDefault(x => MoviesMatch(item.Movie, x));
                if (movie != null)
                {
                    // Update local data from watchlist
                    movie.TraktId = item.Movie.Ids.Trakt ?? 0;
                    movie.Title = item.Movie.Title;
                    movie.Year = item.Movie.Year ?? 0;

                    continue;
                }

                movie = new WatchlistMovie(item.Movie.Title, item.Movie.Year ?? 0, item.Movie.Ids.Trakt ?? 0);
                localWatchlist.Add(movie);
            }

            foreach (var item in localWatchlist)
            {
                // Mark as removed so we can clean them up later
                if (!traktWatchlist.Any(x => MoviesMatch(x.Movie, item)))
                {
                    item.Removed = true;

                    // TODO: Make it an option to cancel scheduled recordings if an item is removed?
                }
            }

            // Find anything marked as removed that either:
            // a. hasn't been scheduled yet, or
            // b. is finished recording.
            // Those are safe to clean up.
            var purgeList = localWatchlist.Where(x => x.Removed && (!x.Scheduled || x.Completed)).ToList();
            foreach (var item in purgeList)
            {
                localWatchlist.Remove(item);
            }

            await schedulerDb.SaveChangesAsync();
            await localWatchlist.LoadAsync();
            OnPropertyChanged("Watchlist");
        }

        /// <summary>
        /// Scans the movies found in the EPG for programs matching movies on the watchlist.
        /// </summary>
        /// <param name="watchlist">The watchlist</param>
        /// <returns>An enumerable of tuples containing the watchlist entry and the corresponding program.
        /// Watchlist entries with no matching programs are omitted.</returns>
        private IEnumerable<Tuple<WatchlistMovie, Microsoft.MediaCenter.Guide.Program>> FindProgramsMatchingWatchlist(DbSet<WatchlistMovie> watchlist)
        {
            var movies = GuideMovies;

            foreach (WatchlistMovie movie in watchlist)
            {
                var matching = movies.Where(x => x.Title == movie.Title && x.OriginalAirdate.Year == movie.Year);
                foreach (var m in matching)
                {
                    yield return Tuple.Create(movie, m);
                }
            }
        }

        #endregion

        #region Recordings

        /// <summary>
        /// Searches the EPG for programs matching movies on the watchlist and schedules a recording for the next airing.
        /// </summary>
        public void ScheduleForMovieWatchlist()
        {
            FindMovies();
            var watchlistMovies = FindProgramsMatchingWatchlist(localWatchlist);
            foreach (var m in watchlistMovies)
            {
                if (m.Item1.Scheduled)
                {
                    // Skip a movie if we already scheduled it.
                    continue;
                }

                if(m.Item2.RequestedPrograms.Any())
                {
                    // There is already a request pending, but we don't know about it (manually scheduled, probably?)
                    m.Item1.Scheduled = true;
                    m.Item1.ScheduleId = m.Item2.RequestedPrograms.First().Request.Id;

                    schedulerDb.SaveChanges();
                    continue;
                }

                Request r = ScheduleBestRecording(m.Item2);
                if (r != null)
                {
                    LogEvent(string.Format("Found an upcoming showing of {1} that matches the search criteria.{0}Recording scheduled for {2} on channel {3} {4} {5}",
                        Environment.NewLine,
                        m.Item1,
                        r.StartTime.ToLocalTime(),
                        r.Channel.DisplayChannelNumber,
                        r.Channel.CallSign,
                        r.Channel.Service.IsHDCapable ? "(HD)" : "(SD)"
                        ), eventId: LogEventType.RecordingScheduled);

                    m.Item1.Scheduled = true;
                    m.Item1.ScheduleId = r.Id;

                    schedulerDb.SaveChanges();
                }
            }
        }

        /// <summary>
        /// Schedules the next airing of a program to record.
        /// </summary>
        /// <remarks>Currently assumes that only HD programs are of interest.</remarks>
        /// <param name="program">The program to record.</param>
        /// <returns>The recording request.</returns>
        private Request ScheduleBestRecording(Microsoft.MediaCenter.Guide.Program program)
        {
            // TODO: Make the criteria configurable
            var scheduleEntry = program.ScheduleEntries.OrderBy(x => x.StartTime).First(x => FutureAiring(x) && x.Service.IsHDCapable);
            return wmcData.ScheduleOneTimeRecording(scheduleEntry);
        }

        /// <summary>
        /// Searches through the scheduled recordings for any completed recordings, and marks them as complete.
        /// Also adds found items to the Trakt collection.
        /// </summary>
        public async void FindCompletedRecordings()
        {
            foreach (var movie in localWatchlist)
            {
                if (movie.Scheduled && !movie.Completed)
                {
                    var request = wmcData.Fetch(movie.ScheduleId) as Request;
                    if (request != null && request.Recordings.Any())
                    {
                        foreach (Recording rec in request.Recordings)
                        {
                            // TODO: Should we try to detect failed recordings and reschedule?
                            if (rec.State == RecordingState.Recorded && !rec.BeganLate && !rec.EndedEarly)
                            {
                                movie.Completed = true;

                                if (Properties.Settings.Default.TraktAddCompleted)
                                {
                                    await Client.Sync.AddToCollectionAsync(new TraktMovieWithCollectionMetadata
                                    {
                                        Title = movie.Title,
                                        Year = movie.Year,
                                        Ids = new TraktMovieIds { Trakt = movie.TraktId },
                                        CollectedAt = rec.EndTime,
                                        MediaType = TraktSharp.Enums.TraktMediaType.Digital,
                                        Is3D = rec.ScheduleEntry.Is3D,
                                    });

                                    LogEvent(string.Format("Recording of {0} completed. Adding to Trakt collection.",
                                        movie), eventId: LogEventType.RecordingCompleted);
                                }
                                break;
                            }
                        }
                    }

                }
            }

            schedulerDb.SaveChanges();
        }

        #endregion
    }
}
