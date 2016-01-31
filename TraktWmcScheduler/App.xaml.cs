using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TraktWmcScheduler
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TraktWmcScheduler");
        static readonly string dbName = "Scheduler.db";
            
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Set the directory to use to store the local database
            
            AppDomain.CurrentDomain.SetData("DataDirectory", dataDir);

            // Check to see if the database exists already. If not, copy the template from the application directory.
            // Note that this DOES NOT replace an existing version, so any updates aren't copied (yet).
            if(!File.Exists(Path.Combine(dataDir, dbName))) {
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbName), Path.Combine(dataDir, dbName));
            }

            if (e.Args.Length >= 1)
            {
                if (e.Args[0] == "/runnow")
                {
                    // Update the Watchlist from Trakt and schedule recordings, but skip the UI (for scheduled tasks)
                    bool success = false;
                    SchedulerController scheduler = null;
                    try
                    {
                        scheduler = new SchedulerController();
                        scheduler.Init();
                        scheduler.DoAutoUpdate();
                        success = true;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (scheduler != null)
                        {
                            scheduler.Dispose();
                        }
                    }

                    Shutdown(success ? 0 : 1);
                }
            }

            // Put up the main UI window
            new MainWindow().Show();
        }
    }
}
