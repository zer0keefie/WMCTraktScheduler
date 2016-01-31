using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TraktWmcScheduler
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        internal SchedulerController Scheduler { get; set; }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Load(object sender, RoutedEventArgs e)
        {
            Scheduler = new SchedulerController();
            Scheduler.LogMessage += Scheduler_LogMessage;
            
            this.DataContext = Scheduler;
            
            try
            {
                Scheduler.Init();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Exception");
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            this.Dispose();
        }

        void Scheduler_LogMessage(object sender, string message)
        {
            this.DebugText.AppendText(message + Environment.NewLine);
        }

        private void Authenticate_Click(object sender, RoutedEventArgs e)
        {
            Scheduler.Authenticate();
        }

        private void LoadEpg_Click(object sender, RoutedEventArgs e)
        {
            Scheduler.FindMovies();
        }

        private void LoadWatchlist_Click(object sender, RoutedEventArgs e)
        {
            Scheduler.GetWatchlist();
        }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            Scheduler.ScheduleForMovieWatchlist();
        }

        private void Complete_Click(object sender, RoutedEventArgs e)
        {
            Scheduler.FindCompletedRecordings();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow about = new AboutWindow();
            about.Owner = this;
            about.ShowDialog();
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (Scheduler != null)
            {
                Scheduler.Dispose();
            }
        }

        #endregion
    }
}
