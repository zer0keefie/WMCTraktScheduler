using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.MediaCenter.Guide;
using Microsoft.MediaCenter.Pvr;
using Microsoft.MediaCenter.Store;

namespace TraktWmcScheduler.MediaCenter
{
    public class WMCDataSource : IDisposable
    {
        public const int RecordQualityBest = 3;

        private ObjectStore os;
        private Scheduler recScheduler;

        private Lazy<List<Channel>> channels;

        public WMCDataSource()
        {
            SHA256 sha = SHA256Managed.Create();

            // This is the magic way to get R/W access to the database.  Cannot be guaranteed to work in future versions of windows
            string providerName = Encoding.Unicode.GetString(Convert.FromBase64String("QQBuAG8AbgB5AG0AbwB1AHMAIQBVAHMAZQByAA=="));

            string epgClientID = ObjectStore.GetClientId(true);
            byte[] epgBytes = Encoding.Unicode.GetBytes(epgClientID);
            byte[] epgHashed = sha.ComputeHash(epgBytes);
            string providerPassword = Convert.ToBase64String(epgHashed);
            os = ObjectStore.Open(providerName, providerPassword);

            recScheduler = new Scheduler(os, ScheduleConflictSource.AutomaticUpdate);

            channels = new Lazy<List<Channel>>(GetAllChannels);
        }

        private static bool IsHiddenChannel(Channel channel)
        {
            if ((channel.UserBlockedState == UserBlockedState.Blocked) || (channel.UserBlockedState == UserBlockedState.Disabled))
                return true;

            if (channel.IsSuggestedBlocked)
                return true;

            if (channel.Visibility == ChannelVisibility.NotTunable)
                return true;

            if (channel.Visibility == ChannelVisibility.Blocked)
                return true;

            if (channel.ChannelType == ChannelType.UserHidden)
                return true;

            return false;
        }

        private List<Channel> GetAllChannels()
        {
            List<Channel> allChannels = new List<Channel>();

            MergedLineups ml = new MergedLineups(os);
            foreach (Lineup lu in ml)
            {
                if (lu == null)
                {
                    Debug.WriteLine("Null lineup found within mergedLineups");
                    continue;
                }

                Channel[] channels = lu.GetChannels();
                foreach (Channel c in channels)
                {
                    if (c != null && !IsHiddenChannel(c))
                    {
                        allChannels.Add(c);
                    }
                }
            }

            return allChannels;
        }

        private IEnumerable<ScheduleEntry> GetMovies(Channel channel, DateTime startTime, DateTime endTime)
        {
            Service svc = channel.Service;
            if (svc == null) yield break;

            ScheduleEntry[] entries = svc.GetScheduleEntriesBetween(DateTime.Today, DateTime.Today.AddDays(1));
            foreach (ScheduleEntry se in entries)
            {
                if (se.Program.IsMovie)
                {
                    yield return se;
                }
            }
        }

        public ISet<Program> GetAllMovies(DateTime startTime, DateTime endTime)
        {
            var allMovies = new HashSet<Program>();

            foreach (Channel c in channels.Value)
            {
                IEnumerable<ScheduleEntry> movies = GetMovies(c, startTime, endTime);
                foreach (ScheduleEntry se in movies)
                {
                    allMovies.Add(se.Program);
                }
            }

            return allMovies;
        }

        public Request ScheduleOneTimeRecording(ScheduleEntry scheduleEntry)
        {
            var channel = scheduleEntry.Service.GetBestChannel(channels.Value);
            OneTimeRequest req = recScheduler.CreateOneTimeRequest(scheduleEntry, channel);

            // Add 5 minutes of padding on either end
            req.PrePaddingRequested = TimeSpan.FromSeconds(300);
            req.PostPaddingRequested = TimeSpan.FromSeconds(300);

            req.Quality = RecordQualityBest;

            req.UpdateRequestedPrograms();
            UpdateDelegate upd = new UpdateDelegate(ScheduleRecording_Done);
            req.UpdateAndSchedule(upd, recScheduler);

            return req;
        }

        /// <summary>
        /// Callback to commit recording data after making a request.
        /// </summary>
        private void ScheduleRecording_Done()
        {
            recScheduler.Schedule();
        }

        public StoredObject Fetch(long id)
        {
            return os.Fetch(id);
        }

        #region IDisposable Members

        public void Dispose()
        {
            this.os.Dispose();
            this.recScheduler.Dispose();
        }

        #endregion
    }
}
