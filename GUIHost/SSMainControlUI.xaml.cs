using MainClass;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace GUIHost
{
    /// <summary>
    /// Interaction logic for SSMainControlView.xaml
    /// </summary>
    public partial class SSMainControlUI : Window
    {
        private App dataToolApp;

        /* Simulator Objects */
        protected TransitDataToolClass currentModel;

        /* Functional Windows&Dialogs */
        //protected SSMapViewerUI surfaceSimulatorMapWindow;

        /* Paths */
        string startupFolderSS;
        //string gpsJSONFolderSS;
        string gpsPath;
        string gtfsPath;
        string gpsCSVFolderSS;
        //string gtfsFolder;
        string simulatorDatabasefileName;// = "SSim-TTC.db";//static
        static string GTFSdatabasefileName = "TTC-GTFS.db";

        /* Web Crawler Settings */
        static bool online = false;
        static bool generateNewGPSdb = false;//default false
        static bool generateNewGPScsv = false;//default false
        static bool generateNewGTFSdb = false;//default false
        //static long dataPacketIncre = 3600;//1 hr size for API data request
        //static long dataPackageIncre = 86400;//1 day size for JSON file
        static int settingTimeStep = 5;
        static int settingMinDataSizePerLink = 0;

        /* Data Processing Setting */
        private bool noNewDataWereLoaded;
        static int dataProcessingMode = 1;
        static bool printResultLog = false;//for outputs to "...\AppData\logs"
        static bool mergeShortLinks = true;
        static double minLinkDistForMerge = 1000;
        //0-Link Construction only (later runs), 1-Data Processing only, 2-Both (first time run)

        /*Other Global Objects */
        TimeSpan sysStart;

        /*Threads for management*/
        Thread downloadThread;

        public SSMainControlUI()
        {
            //INSERT DEBUG CODE HERE:


            //END DEBUG CODE

            InitializeComponent();

            dataToolApp = Application.Current as App;

            updateSettings();

            ////test text box
            //for (int i = 0; i < 100; i++)//test
            //    runLogSS.AppendText("Surface Simulator is running.");
            runLogSS.UpdateLayout();

            //Step 1: initiate
            noNewDataWereLoaded = true;
            simulatorDatabasefileName = "DataToolDb-TTC.db";//default for first session
            currentModel = new TransitDataToolClass(startupFolderSS, gpsPath, simulatorDatabasefileName,
                generateNewGPSdb, generateNewGPScsv, gtfsPath, GTFSdatabasefileName,
                generateNewGTFSdb, settingTimeStep, settingMinDataSizePerLink);
            currentModel.SetDataValidation(true);
            currentModel.SetGPSDataSource(true);
            currentModel.SetLogSetting(false);
            //currentModel.setLogSetting(true);
            updateProgressStatus("Transit Data Tool running...\n", 100, 0);

            //keep some buttons disabled until offline data are loaded
            setMenuButtonState(MenuButtonState.Default);

            currentModel.TaskUpdate += c_SSimulatorTaskUpdate;//event listener
            currentModel.LogUpdate += c_SSimulatorLogUpdate;//event listener
            currentModel.NextbusWebCrawler.LogUpdate += c_SSimulatorLogUpdate;//event listener
            currentModel.TorInciWebCrawler.LogUpdate += c_SSimulatorLogUpdate;//event listener
            currentModel.OpenWeatherWebCrawler.LogUpdate += c_SSimulatorLogUpdate;//event listener
        }

        enum MenuButtonState { Default, Operating, DataLoaded, DataProcessed }
        private void setMenuButtonState(MenuButtonState state)
        {
            if (state.Equals(MenuButtonState.Default))
            {
                //default state
                Button_DownloadLiveData.IsEnabled = true;
            }
            else if (state.Equals(MenuButtonState.Operating))
            {
                //data loaded
                Button_DownloadLiveData.IsEnabled = false;
            }
            else if (state.Equals(MenuButtonState.DataLoaded))
            {
                //data loaded
                Button_DownloadLiveData.IsEnabled = true;
            }
            else if (state.Equals(MenuButtonState.DataProcessed))
            {
                //data loaded and processed
                Button_DownloadLiveData.IsEnabled = true;
            }
        }

        private void downloadRealtimeData_Click(object sender, RoutedEventArgs e)
        {
            //currentModel.TaskUpdate += c_SSimulatorTaskUpdate;//event listener
            SSProgressBar.SetPercent(0, 0);

            //keep button disabled during run
            setMenuButtonState(MenuButtonState.Operating);

            updateSettings();//ensure setting is up to date

            string message = "Confirm that the live data download setting is updated.";
            string caption = "Confirmation";
            MessageBoxButton buttons = MessageBoxButton.OKCancel;
            MessageBoxImage icon = MessageBoxImage.Question;
            if (MessageBox.Show(message, caption, buttons, icon) == MessageBoxResult.OK)
            {
                //Info on multithreading https://msdn.microsoft.com/en-us/library/mt679047.aspx
                downloadThread = new Thread(new ThreadStart(downloadRealtimeDataOperations));
                downloadThread.Start();

                //while (!dataProcessThread.ThreadState.Equals(ThreadState.Stopped))
                //{
                //}
            }
            else
            {
                //enable buttons after run
                setMenuButtonState(MenuButtonState.Default);
                SSProgressBar.SetPercent(100, 0.2);
                runLogSS.AppendText("Data Download Aborted.\n");
            }
        }
        private void downloadRealtimeDataOperations()
        {
            //Step 2: download and load raw data
            //do 3 different download tasks separately
            #region ParallelTasks
            Parallel.Invoke(() =>
            {
                currentModel.DownloadNextBusGPSData();
            },  // close first Action
            () =>
            {
                currentModel.DownloadTorontoIncidentData();
            }, //close second Action
            () =>
            {
                currentModel.DownloadWeatherData();
            } //close third Action,
              //() => {} //close forth Action
            ); //close parallel.invoke
            #endregion
            this.Dispatcher.Invoke(() =>
            {
                setMenuButtonState(MenuButtonState.Default);
                SSProgressBar.SetPercent(100, 1);
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        
        private void FileExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            currentModel.CloseDatabase();
            if (downloadThread != null)
            {
                downloadThread.Abort();
            }
            Application.Current.Shutdown();
            //Environment.Exit(0);
        }

        private void updateProgressStatus(string statusMessage, double progressPercent, double progressDurationInSecs)
        {
            this.Dispatcher.Invoke(() =>
            {
                runStatusSS.AppendText(statusMessage);
                SSProgressBar.SetPercent(progressPercent, progressDurationInSecs);
            });
        }
        private void updateSettings()
        {
            startupFolderSS = Directory.GetCurrentDirectory() + @"\" + @"AppData\";
            gtfsPath = startupFolderSS + @"GTFS" + @"\";
            gpsPath = startupFolderSS + @"GPS" + @"\";
        }

        // Event Handling from External classes
        void c_SSimulatorTaskUpdate(object sender, TaskUpdateEventArgs e)
        {
            if (e.eventMessage.Equals(""))
            {
                updateProgressStatus("", e.progressBarPercent, e.expectedDuration);
            }
            else
            {
                updateProgressStatus(string.Format("{0} ({1})\n", e.eventMessage, e.timeReached.DateTimeISO8601Format()), e.progressBarPercent, e.expectedDuration);
            }
        }
        void c_SSimulatorLogUpdate(object sender, LogUpdateEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                runLogSS.AppendText(string.Format("{0}\n", e.logMessage));
            });
        }
    }
    public static class ProgressBarExtensions
    {
        public static void SetPercent(this ProgressBar progressBar, double percentage, double durationInSecs = 2)
        {
            TimeSpan duration = TimeSpan.FromSeconds(durationInSecs);
            DoubleAnimation animation = new DoubleAnimation(percentage, duration.Duration());
            progressBar.BeginAnimation(ProgressBar.ValueProperty, animation);
        }
    }
}
