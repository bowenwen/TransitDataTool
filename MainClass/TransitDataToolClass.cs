using Microsoft.Win32;
using MainClass.DataTools;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MainClass
{
    /* 
     * AUTHOR
     * - BO WEN WEN, UNIVERSITY OF TORONTO
     * 
     * NOTICES
     * - See GitHub project page.
     * 
     * MAIN FUNCTIONALITIES
     * - open, modify and closes the databases. 
     * - downloads open transit data for Toronto.
     * 
    */
    public class TransitDataToolClass
    {
        #region GLOBAL VARIABLE DEFINITIONS
        /* Misc. */
        private Random RandomNumGen;
        /* Trackers */
        private int DeleteActionTracker;
        /* Path Configuration - from host */
        private string MainFolderPath;
        private string GpsJSONPath;
        private string GpsCSVPath;
        private string GpsXMLPath;
        private string RoadRestrictionPath;
        private string WeatherDataPath;
        private bool GenerateCSVFile;
        private string DbFile_Simulator;
        private string LogFileFolder;
        private bool StartNewGPSdbFiles;
        private bool StartNewGTFSdbFiles;
        private bool WriteToLog;
        private bool ValidateModel;
        private bool LoadDatabaseToMemory;
        private int DbConn_numChanges;
        //private SQLiteConnection dbConn_GPS;//global doesn't work well
        //private SQLiteConnection dbConn_GTFS;

        /* Web Crawler Setting - from host - TTC ONLY */
        public SSWebCrawlerCVST CvstWebCrawler { get; set; }
        public SSWebCrawlerNextBus NextbusWebCrawler { get; set; }
        public SSWebCrawlerTORdRestr TorInciWebCrawler { get; set; }
        public SSWebCrawlerOpenWeather OpenWeatherWebCrawler { get; set; }
        /// <summary>
        /// A API key for downloading from OpenWeatherMap API.
        /// NOTE: Please register and enter your own, the default api key may not work.
        /// </summary>
        public string OpenWeatherAPIKey = "6ff5a71f8cc2f8a68e3717c5a5d33e77";
        private long CVSTDataDownloadIncre = 3600;//1 hr size for API data request
        private long CVSTdataStorageIncre = 86400;//1 day size for JSON file
        private long GpsPollingFreq = 10;//20;
        private long GpsStorageIncreInSecs = 3600;//1 hr size for all route gps data save
        private long GpsFileBackupIncrenSecs = 1 * 60;//1 minute data backup
        private long IncidentPollingFreq = 60;
        //private long incidentStorageIncreInSecs = 3600 * 24;//24 hr size for all route incident data save
        private long IncidentFileBackupIncrenSecs = 5 * 60;//5 minute data backup
        private long WeatherPollingFreq = 10 * 60;//note: openweather requests that only one request per 10 mins from single device/api key
        //private long weatherStorageIncreInSecs = 3600 * 24;//24 hr size for all route incident data save
        private long WeatherFileBackupIncrenSecs = 10 * 60;//10 minute data backup
        //static long dataPacketIncre = 3600;//1 hr size for API data request
        //static long dataPackageIncre = 86400;//1 day size for JSON file
        public List<DateRange> TrainingDateRanges { get; private set; }
        public List<DateRange> TestSetDateRanges { get; private set; }
        private List<DateRange> AllDateRanges;
        private bool UseNextBusGPSData;

        /* Data Processing Setting */
        private bool IncludeOneHourWarmUp;
        private double ClusterDisplacementTolerance = 50.0;
        private double DwellTimeBufferRadius = 50.0 / 2;//note it is halves, it is the radius of boundary.
        private bool RandomTestSample = false;
        private double TestSetFraction = 0.2;

        /* Simulator Data Object */
        // Data (processed) from various sources: AVL, weather, incident, Intxn
        public ConcurrentDictionary<int, SSLinkObjectTable> LinkObjectTable { get; set; }
        public ConcurrentDictionary<Tuple<int, int, int>, int> LinkIDIndexByStartEndAndScheduleID { get; set; } // passively updated as linkObjectTable is being constructed, use as index to prevent duplicate links
        public ConcurrentDictionary<int, SSVehTripDataTable> GpsTripTable { get; set; } // vehicle trips, index by TripID, used for processing
        public ConcurrentDictionary<int, SSVehGPSDataTable> GpsPointTable { get; set; } // GPS locations, index by gpsID, used for processing
        public ConcurrentDictionary<string, SSIncidentDataTable> RdIncidentsTable { get; set; } // road Restrictions, index by IncidentID, used for processing
        public ConcurrentDictionary<int, SSWeatherDataTable> WeatherTable { get; set; } // weather condition of city locations, index by SQID, used for processing
        public ConcurrentDictionary<int, SSINTNXSignalDataTable> IntxnSignalTable { get; set; } // Intxn locations and counts for toronto, index by px ID
        //Additional data properties
        /// <summary>
        /// stores string description of the stop location. Use: "nearSide", "farSide", "midblock"
        /// (determined and stored during run time (data processing with links), not part of gtfs database)
        /// </summary>
        public ConcurrentDictionary<int, string> StopLocTypStringByStopID { get; set; }//string stopLocation { get; set; }
        // public ConcurrentDictionary<Tuple<int, int>, List<int>> IntxnIDsByStartAndEndStopIDs { get; set; }

        /* Data Processing Objects */
        /// <summary>
        /// Database connection to the main simulator db file - on memory
        /// </summary>
        private SQLiteConnection DbConn_Simulator;
        // Call openSimulatorDbConnection() to open, Call saveSimulatorDbChanges() to save changes to db on mem, Call closeSimulatorDbConnection() to save and close db on mem
        /* Data Processing Settings and Trackers */
        /// <summary>
        /// Stop IDs to be removed from data processing, model estimation and analysis 
        /// (Global setting that can be modified from external calls)
        /// Default: exculde all stops with type timing_stop
        /// </summary>
        public List<int> GlobalExceptStops { get; set; }
        /// <summary>
        /// in minutes - being static, it is shared in all instances of SSimulator objects
        /// </summary>
        private static int SettingTimeStep;
        private int GpsPointCurrentMaxID;//the max id of the current GPS database
        private int GpsTripCurrentMaxID;//the max id of the current GPS Trips database
        //private int GPSLinkGlobalID;//the size of GPS links database, note links are not stored and is only constructed and read in runtime - need to be changed later (separate db file maybe needed)
        //private int lastSessionTripID;//the ID of the last TripID from last session
        private int LastSessionGPSDataID;//the ID of the last GPSID from last session
        
        #endregion

        #region STEP 1: INITIATION METHODS (CONSTRUCTOR)

        /// <summary>
        /// initiate objects and values for data objects, web crawler and database management
        /// </summary>
        /// <param name="mainFolderPathloc">The path of main folder.</param>
        /// <param name="gpsPathLoc">The path of GPS database.</param>
        /// <param name="databasefileName">The filename of GPS database.</param>
        /// <param name="newGPSFile">forces the creation of new GPS database file even if one exist (recommended for batch processing procedures).</param>
        /// <param name="generateGPSCSV">Generate CSV data file of all GPS points after GPS data has been loaded into database.</param>
        /// <param name="gtfsPathLoc">The path of GTFS database.</param>
        /// <param name="GTFSdatabasefileName">The filename of GTFS database.</param>
        /// <param name="newGTFSFile">forces the processing of new GTFS even if one already exist.</param>
        /// <param name="timeStep">(Not Used) the time step used for data polling and processing.</param>
        /// <param name="minDataSizeForLink">(Not Used) for internal model estimation, the mimimum data size per link.</param>
        /// <param name="includeOneHourWarmUpData">in addition to specified date ranges, should the program include one hour warm up data for each date range.</param>
        /// <param name="initialTrainingDateRanges">default: null. Manually specify training date ranges, instead of read from setting.</param>
        /// <param name="initialTestSetDateRanges">default: null. Manually specify test data date ranges, instead of read from setting.</param>
        /// <param name="includeOneHourWarmUpData">default: true. Include one hour warm up before start in data processing.</param>
        /// <param name="randomlyAssignTestSample">default: false. Randomly assign data as test sample (20% hard-coded).</param>
        public TransitDataToolClass(string mainFolderPathloc, string gpsPathLoc, string databasefileName, bool newGPSFile, bool generateGPSCSV, string gtfsPathLoc, string GTFSdatabasefileName, bool newGTFSFile, int timeStep, int minDataSizeForLink, List<DateRange> initialTrainingDateRanges = null, List<DateRange> initialTestSetDateRanges = null, bool includeOneHourWarmUpData = true, bool randomlyAssignTestSample = false)
        {
            //set random number seed
            RandomNumGen = new Random(100);

            IncludeOneHourWarmUp = includeOneHourWarmUpData;
            //Trackers
            DeleteActionTracker = 0;
            //Path strings
            MainFolderPath = mainFolderPathloc;
            GpsJSONPath = gpsPathLoc + @"GPS_JSON\";
            GpsXMLPath = gpsPathLoc + @"GPS_XML\";
            GpsCSVPath = gpsPathLoc + @"GPS_CSV\";
            string incidentPathMaster = mainFolderPathloc + @"INCI\";
            RoadRestrictionPath = mainFolderPathloc + @"INCI\" + @"toronto\";
            WeatherDataPath = mainFolderPathloc + @"HISTWEA\";

            LogFileFolder = mainFolderPathloc + @"logs\";

            //CSV file setting
            GenerateCSVFile = generateGPSCSV;

            //File Folder Check and Initiation
            if (!Directory.Exists(mainFolderPathloc))
            {
                Directory.CreateDirectory(mainFolderPathloc);
            }
            //if (!Directory.Exists(gtfsPathLoc))
            //{
            //    Directory.CreateDirectory(gtfsPathLoc);
            //}
            if (!Directory.Exists(GpsJSONPath))
            {
                Directory.CreateDirectory(GpsJSONPath);
            }
            if (!Directory.Exists(GpsXMLPath))
            {
                Directory.CreateDirectory(GpsXMLPath);
            }
            if (!Directory.Exists(GpsCSVPath))
            {
                Directory.CreateDirectory(GpsCSVPath);
            }
            if (!Directory.Exists(incidentPathMaster))
            {
                Directory.CreateDirectory(incidentPathMaster);
            }
            if (!Directory.Exists(RoadRestrictionPath))
            {
                Directory.CreateDirectory(RoadRestrictionPath);
            }
            if (!Directory.Exists(WeatherDataPath))
            {
                Directory.CreateDirectory(WeatherDataPath);
            }
            if (!Directory.Exists(LogFileFolder))
            {
                Directory.CreateDirectory(LogFileFolder);
            }

            //Modelling Settings
            SettingTimeStep = timeStep;
            UseNextBusGPSData = true;
            StartNewGPSdbFiles = false;
            StartNewGPSdbFiles = newGPSFile;
            StartNewGTFSdbFiles = false;
            StartNewGTFSdbFiles = newGTFSFile;
            WriteToLog = false;
            ValidateModel = true;
            
            //DataTools
            CvstWebCrawler = new SSWebCrawlerCVST(MainFolderPath, GpsJSONPath, CVSTDataDownloadIncre);//wouldn't get or update file if file is not missing and is not online
            NextbusWebCrawler = new SSWebCrawlerNextBus(GpsXMLPath, GpsStorageIncreInSecs, GpsFileBackupIncrenSecs);
            TorInciWebCrawler = new SSWebCrawlerTORdRestr(RoadRestrictionPath, IncidentFileBackupIncrenSecs);//, incidentStorageIncreInSecs
            OpenWeatherWebCrawler = new SSWebCrawlerOpenWeather(WeatherDataPath, WeatherFileBackupIncrenSecs, OpenWeatherAPIKey);//, weatherStorageIncreInSecs

            //Analysis Date Range - either get from source files or from constructor initialization inputs
            TrainingDateRanges = initialTrainingDateRanges == null ? GetListofDateRange("DateRange.txt") : initialTrainingDateRanges;//for training data set - read from source
            TestSetDateRanges = initialTestSetDateRanges == null ? GetListofDateRange("DateRange-test.txt") : initialTestSetDateRanges;//for testing data set - read from source

            //Initialize All Date Range to get 
            AllDateRanges = new List<DateRange>();
            AllDateRanges.AddRange(ObjectCopier.Clone(TrainingDateRanges));
            AllDateRanges.AddRange(ObjectCopier.Clone(TestSetDateRanges));
            //allDateRanges.AddRange(trainingDateRanges);
            //allDateRanges.AddRange(testSetDateRanges);
            AllDateRanges = AllDateRanges.OrderBy(o => o.start.DateTimeToEpochTime()).ToList();//uses LINQ, may also use OrderByDescending

            //GPS Database initializations
            string databaseFilePath = Path.Combine(MainFolderPath, databasefileName);
            DbFile_Simulator = databaseFilePath;
            // determine if it is suitable to load application database to memory
            LoadDatabaseToMemory = false;
            // determine if database should be on disk or in memeory by duration of data
            double totalHours = 0.0;
            foreach (DateRange aRange in AllDateRanges)
            {
                totalHours += aRange.getDurationHours();
            }
            if (totalHours < 5)//Assumes min of 8GB ram
            {
                LoadDatabaseToMemory = true;
            }

            //Analysis Date Range modifications
            // Warm Up data period: adding one hour to start (Note: the original training and test set date ranges are not modified!)
            if (IncludeOneHourWarmUp)
            {
                for (int i = 0; i < AllDateRanges.Count; i++)
                {
                    AllDateRanges[i].start = AllDateRanges[i].start.AddHours(-1);
                }
            }

            //Method of drawing test sample
            RandomTestSample = randomlyAssignTestSample;

            ////if file doesn't exist, save to disk to be safe
            //if (File.Exists(dbFile_Simulator))
            //{
            //    long length = new System.IO.FileInfo(dbFile_Simulator).Length / 1048576;//MB
            //    if (length < 350)
            //    {
            //        //if database is smaller than 500MB, don't even bother to check System.Diagnostics (to save time) - assume computers have at least 4GB total mem (or 2GB free)
            //        loadDatabaseToMemory = true;
            //    }
            //    else
            //    {
            //        loadDatabaseToMemory = false;
            //        //System.Diagnostics.PerformanceCounter memoryQuery = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes", true);
            //        //long availMem = Convert.ToInt64(memoryQuery.NextValue());//MB
            //        //memoryQuery.Dispose();
            //        //database should take no more than 1/4 of available mem, since processing will need free mem
            //        //if ((availMem * 1 / 6) > (length))
            //        //{
            //        //    loadDatabaseToMemory = true;
            //        //}
            //        //Commented out due to known issue: http://stackoverflow.com/questions/17980178/cannot-load-counter-name-data-because-an-invalid-index-exception
            //    }
            //}
            OpenSimulatorDbConnection();

            //object initializations
            GpsTripTable = new ConcurrentDictionary<int, SSVehTripDataTable>();
            GpsPointTable = new ConcurrentDictionary<int, SSVehGPSDataTable>();
            RdIncidentsTable = new ConcurrentDictionary<string, SSIncidentDataTable>();
            WeatherTable = new ConcurrentDictionary<int, SSWeatherDataTable>();
            IntxnSignalTable = new ConcurrentDictionary<int, SSINTNXSignalDataTable>();
            LinkObjectTable = new ConcurrentDictionary<int, SSLinkObjectTable>();
            LinkIDIndexByStartEndAndScheduleID = new ConcurrentDictionary<Tuple<int, int, int>, int>();
            StopLocTypStringByStopID = new ConcurrentDictionary<int, string>();

            GpsPointCurrentMaxID = SSUtil.GetTableMaxID(DbFile_Simulator, "TTCGPS", "GPSID");//size of database table
            GpsTripCurrentMaxID = SSUtil.GetTableMaxID(DbFile_Simulator, "TTCGPSTRIPS", "TripID");//get GPSTripGlobalID

            //lastSessionTripID = GPSTripGlobalID;
            LastSessionGPSDataID = GpsPointCurrentMaxID;

            //prepare logString
            logString = new StringBuilder();
        }
        private byte[] ToByteArray(object source)
        {
            if (source == null)
            {
                return null;
            }
            else
            {
                var formatter = new BinaryFormatter();
                using (var stream = new MemoryStream())
                {
                    formatter.Serialize(stream, source);
                    return stream.ToArray();
                }
            }
        }
        private Object ByteArrayToObject(byte[] arrBytes)
        {
            MemoryStream memStream = new MemoryStream();
            BinaryFormatter binForm = new BinaryFormatter();
            memStream.Write(arrBytes, 0, arrBytes.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            Object obj = (Object)binForm.Deserialize(memStream);

            return obj;
        }
        private List<DateRange> GetListofDateRange(string txtfilename)
        {
            List<DateRange> outputDateRange = new List<DateRange>();
            if (File.Exists(MainFolderPath + txtfilename))
            {
                DateTime startTime = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                DateTime endTime = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                DateTime startDate = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                DateTime endDate = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                startTime = startTime.ToLocalTime();
                endTime = endTime.ToLocalTime();
                startDate = startDate.ToLocalTime();
                endDate = endDate.ToLocalTime();
                //read the range of date and the range of time
                List<DateTime> dateList = new List<DateTime>();
                string datePattern = @"(?<date>(\d){4}-(\d){2}-(\d){2})"; //Define regex string
                                                                          //date pattern
                Regex reg = new Regex(datePattern); //read log content
                string Content = File.ReadAllText(MainFolderPath + txtfilename);
                MatchCollection matches = reg.Matches(Content); //run regex
                foreach (Match m in matches) //iterate over matches
                {
                    DateTime date = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                    date = DateTime.Parse(m.Groups["date"].Value);
                    dateList.Add(date);
                }            //local time is assumed to be entered
                             //startDate = dateList[0].Date;//check local time
                             //endDate = dateList[1].Date;//check local time
                List<DateTime> timeList = new List<DateTime>();
                string timePattern = @"(?<date>(\d){2}:(\d){2}:(\d){2})"; //Define regex string
                                                                          //time pattern
                reg = new Regex(timePattern); //read log content
                Content = File.ReadAllText(MainFolderPath + txtfilename);
                matches = reg.Matches(Content); //run regex
                foreach (Match m in matches) //iterate over matches
                {
                    DateTime date = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                    date = DateTime.Parse(m.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                    timeList.Add(date);
                }            //local time is assumed to be entered
                             //start and end times kept the same
                startTime = timeList[0].ToLocalTime();
                endTime = timeList[1].ToLocalTime();

                //construct full date range - assuming dates are consecutive
                decimal aDec = dateList.Count / 2;
                int numDateRangeCombination = Convert.ToInt32(Math.Round((aDec), 0));
                for (int n = 0; n < numDateRangeCombination; n++)//date range sets (weeks)
                {
                    startDate = dateList[2 * n].Date;//check local time
                    endDate = dateList[2 * n + 1].Date;//check local time
                                                       //define DateTime for download in UTC
                    int numDays = Convert.ToInt32((endDate - startDate).TotalDays) + 1;//plus 1 to include first day
                    DateTime TempStartDateTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, DateTimeKind.Local);
                    DateTime TempEndDateTime = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, DateTimeKind.Local);
                    TempStartDateTime = TempStartDateTime.AddHours(startTime.Hour);
                    TempStartDateTime = TempStartDateTime.AddMinutes(startTime.Minute);
                    TempStartDateTime = TempStartDateTime.AddSeconds(startTime.Second);
                    TempEndDateTime = TempEndDateTime.AddHours(endTime.Hour);
                    TempEndDateTime = TempEndDateTime.AddMinutes(endTime.Minute);
                    TempEndDateTime = TempEndDateTime.AddSeconds(endTime.Second);
                    for (int i = 0; i < numDays; i++)//include first and last day for a date range (days)
                    {
                        DateRange TempRange = new DateRange();
                        TempRange.start = TempStartDateTime.ToUniversalTime();
                        TempRange.end = TempEndDateTime.ToUniversalTime();
                        outputDateRange.Add(TempRange);
                        TempStartDateTime = TempStartDateTime.AddDays(1);
                        TempEndDateTime = TempEndDateTime.AddDays(1);
                    }
                }
            }
            return outputDateRange;
        }
        ///// <summary>
        ///// determines whether data belongs to training or test
        ///// </summary>
        ///// <param name="aDateTime"></param>
        ///// <returns></returns>
        //private bool isDateTimeForTrainingSet(DateTime aDateTime)
        //{
        //    aDateTime.ToUniversalTime();
        //    bool isTrainingSet = true;//default is training set
        //    if (trainingDateRanges.Count > 0 && testSetDateRanges.Count > 0)
        //    {
        //        //if time range full within analysisDateRange
        //        //if not part of any testSet period(s), then must be for training data
        //        DateTime testSetStart;
        //        DateTime testSetEnd;
        //        foreach (DateRange aRange in testSetDateRanges)
        //        {
        //            testSetStart = aRange.start.ToUniversalTime();
        //            testSetEnd = aRange.end.ToUniversalTime();
        //            if (testSetStart <= aDateTime && testSetEnd >= aDateTime)
        //                isTrainingSet = false;//if not training set, then is test set
        //        }
        //    }
        //    else
        //    {
        //        return true; //cannot be determined, default as training set
        //    }
        //    return isTrainingSet;
        //}

        #endregion

        #region STEP 2: DATA COLLECTION METHODS
        /// <summary>
        /// collect raw data from JSON or online files and prepare them for processing in SQLite database - LEGACY
        /// </summary>
        /// <param name="online"></param>
        /// <param name="dataPackageTimeIncrement"></param>
        /// <param name="dataPacketTimeIncrement"></param>
        //public void DownloadCVSTJSONData(Boolean online)//long dataPackageTimeIncrement, long dataPacketTimeIncrement
        //{
        //    //retrive data
        //    //cvstWebCrawler = new SSWebCrawlerCVST(startupPath, gpsJSONPath, online, CVSTDataDownloadIncre);//wouldn't get or update file if file is not missing and is not online
        //    CvstWebCrawler.online = online;//update online parameters
        //    List<SSVehGPSDataTable> TempGPSData = new List<SSVehGPSDataTable>(); // for storing GPS data before right before sending it to database

        //    foreach (DateRange dateRange in AllDateRanges)//include first and last day
        //    {
        //        DateTime startDateTime = dateRange.start.ToUniversalTime();
        //        DateTime endDateTime = dateRange.end.ToUniversalTime();

        //        //using a list of routes, we pull JSON data for all of the routes in the list over desired time periods
        //        DateTime intermediateDateTime = startDateTime;
        //        long timeDifference = endDateTime.DateTimeToEpochTime() - startDateTime.DateTimeToEpochTime();
        //        //SSimUtility.DateTimeToEpochTime(startDateTime)
        //        int num_packages = (int)Math.Ceiling((double)timeDifference / (double)CVSTdataStorageIncre);
        //        //time difference should not be greater than 1 day, which is 86400 seconds
        //        if (timeDifference > CVSTdataStorageIncre)
        //        {
        //            for (int j = 0; j < num_packages; j++)
        //            {
        //                if (j == (num_packages - 1))// last package
        //                    TempGPSData = CvstWebCrawler.RetriveGPSData(RouteTagList, intermediateDateTime, endDateTime.AddSeconds(-1));//true for online mode
        //                else
        //                {
        //                    intermediateDateTime = SSUtil.EpochTimeToUTCDateTime(startDateTime.DateTimeToEpochTime() + CVSTdataStorageIncre);
        //                    TempGPSData = CvstWebCrawler.RetriveGPSData(RouteTagList, startDateTime, intermediateDateTime.AddSeconds(-1));//true for online mode
        //                    startDateTime = SSUtil.EpochTimeToUTCDateTime(startDateTime.DateTimeToEpochTime() + CVSTdataStorageIncre);
        //                }
        //            }
        //        }
        //        else //the only package
        //            TempGPSData.AddRange(CvstWebCrawler.RetriveGPSData(RouteTagList, startDateTime, endDateTime.AddSeconds(-1)));//true for online mode
        //    }
        //    CvstWebCrawler.quitWebCrawler();//close and dispose of web driver browser
        //}
        public void DownloadNextBusGPSData()//long dataPackageTimeIncrement, long dataPacketTimeIncrement
        {
            //read settings from text file - root dir
            DateRange dataDownloadPeriod = new DateRange();
            List<DateTime> dateList = new List<DateTime>();
            string datePattern = @"(?<date>(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2}))"; //Define regex string
            Regex reg = new Regex(datePattern); //read log content
            string Content = File.ReadAllText(MainFolderPath + "DataDownload-Setting.txt");//startupFolderSS
            MatchCollection matches = reg.Matches(Content); //run regex
            foreach (Match m in matches) //iterate over matches
            {
                DateTime date = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                date = DateTime.Parse(m.Groups["date"].Value);
                dateList.Add(date);
            }            //local time is assumed to be entered
            dataDownloadPeriod.start = dateList[0];
            dataDownloadPeriod.end = dateList[1];

            long duration = (dataDownloadPeriod.end.DateTimeToEpochTime() - DateTime.Now.DateTimeToEpochTime());
            TaskUpdateEventArgs args = new TaskUpdateEventArgs();
            args.eventMessage = String.Format("NextBus data polling started...");// Time to Completion: {0} mins.", duration / 60
            args.expectedDuration = Convert.ToDouble(duration);
            args.progressBarPercent = 90.0;
            args.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //nextbusWebCrawler = new SSWebCrawlerNextBus(gpsXMLPath, gpsStorageIncreInSecs, gpsFileBackupIncrenSecs);
            int completedDownloadState = NextbusWebCrawler.startLiveDownloadTask(dataDownloadPeriod.start, dataDownloadPeriod.end, GpsPollingFreq);

            //report state of the downloaded data
            TaskUpdateEventArgs args2 = new TaskUpdateEventArgs();
            string TempEventMessage = "";
            if (completedDownloadState == 0)
            {
                TempEventMessage = String.Format("NextBus data polling completed with {0}.", "no error");
            }
            else if (completedDownloadState == -1)
            {
                TempEventMessage = String.Format("NextBus data polling completed with {0}.", "some missing data (specified start date was too early)");
            }
            else
            {
                TempEventMessage = String.Format("NextBus data polling completed with {0}.", "failure (specified end date was too early)");
            }
            args2.eventMessage = TempEventMessage;
            args2.expectedDuration = Convert.ToDouble(1.0);
            args2.progressBarPercent = 95.0;
            args2.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error

        }
        public void DownloadTorontoIncidentData()//long dataPackageTimeIncrement, long dataPacketTimeIncrement
        {
            //read settings from text file - root dir
            DateRange dataDownloadPeriod = new DateRange();
            List<DateTime> dateList = new List<DateTime>();
            string datePattern = @"(?<date>(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2}))"; //Define regex string
            Regex reg = new Regex(datePattern); //read log content
            string Content = File.ReadAllText(MainFolderPath + "DataDownload-Setting.txt");//startupFolderSS
            MatchCollection matches = reg.Matches(Content); //run regex
            foreach (Match m in matches) //iterate over matches
            {
                DateTime date = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                date = DateTime.Parse(m.Groups["date"].Value);
                dateList.Add(date);
            }            //local time is assumed to be entered
            dataDownloadPeriod.start = dateList[0];
            dataDownloadPeriod.end = dateList[1];

            long duration = (dataDownloadPeriod.end.DateTimeToEpochTime() - DateTime.Now.DateTimeToEpochTime());
            TaskUpdateEventArgs args = new TaskUpdateEventArgs();
            args.eventMessage = String.Format("Incident data (TOR) polling started...");// Time to Completion: {0} mins.", duration / 60
            args.expectedDuration = Convert.ToDouble(duration);
            args.progressBarPercent = 90.0;
            args.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //torInciWebCrawler = new SSWebCrawlerTORdRestr(roadRestrictionPath, incidentFileBackupIncrenSecs);//, incidentStorageIncreInSecs
            int completedDownloadState = TorInciWebCrawler.startLiveDownloadTask(dataDownloadPeriod.start, dataDownloadPeriod.end, IncidentPollingFreq);//5 min polling should be sufficient

            //report state of the downloaded data
            TaskUpdateEventArgs args2 = new TaskUpdateEventArgs();
            string TempEventMessage = "";
            if (completedDownloadState == 0)
            {
                TempEventMessage = String.Format("Incident data (TOR) polling completed with {0}.", "no error");
            }
            else if (completedDownloadState == -1)
            {
                TempEventMessage = String.Format("Incident data (TOR) polling completed with {0}.", "some missing data (specified start date was too early)");
            }
            else
            {
                TempEventMessage = String.Format("Incident data (TOR) polling completed with {0}.", "failure (specified end date was too early)");
            }
            args2.eventMessage = TempEventMessage;
            args2.expectedDuration = Convert.ToDouble(1.0);
            args2.progressBarPercent = 95.0;
            args2.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error

        }
        public void DownloadWeatherData()//long dataPackageTimeIncrement, long dataPacketTimeIncrement
        {
            //read settings from text file - root dir
            DateRange dataDownloadPeriod = new DateRange();
            List<DateTime> dateList = new List<DateTime>();
            string datePattern = @"(?<date>(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2}))"; //Define regex string
            Regex reg = new Regex(datePattern); //read log content
            string Content = File.ReadAllText(MainFolderPath + "DataDownload-Setting.txt");//startupFolderSS
            MatchCollection matches = reg.Matches(Content); //run regex
            foreach (Match m in matches) //iterate over matches
            {
                DateTime date = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Local);
                date = DateTime.Parse(m.Groups["date"].Value);
                dateList.Add(date);
            }            //local time is assumed to be entered
            dataDownloadPeriod.start = dateList[0];
            dataDownloadPeriod.end = dateList[1];

            long duration = (dataDownloadPeriod.end.DateTimeToEpochTime() - DateTime.Now.DateTimeToEpochTime());
            TaskUpdateEventArgs args = new TaskUpdateEventArgs();
            args.eventMessage = String.Format("Weather data (GTA) polling started...");// Time to Completion: {0} mins.", duration / 60
            args.expectedDuration = Convert.ToDouble(duration);
            args.progressBarPercent = 90.0;
            args.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //openWeatherWebCrawler = new SSWebCrawlerOpenWeather(weatherDataPath, weatherFileBackupIncrenSecs);//, weatherStorageIncreInSecs
            int completedDownloadState = OpenWeatherWebCrawler.startLiveDownloadTask(dataDownloadPeriod.start, dataDownloadPeriod.end, WeatherPollingFreq);//5 min polling should be sufficient

            //report state of the downloaded data
            TaskUpdateEventArgs args2 = new TaskUpdateEventArgs();
            string TempEventMessage = "";
            if (completedDownloadState == 0)
            {
                TempEventMessage = String.Format("Weather data (GTA) polling completed with {0}.", "no error");
            }
            else if (completedDownloadState == -1)
            {
                TempEventMessage = String.Format("Weather data (GTA) polling completed with {0}.", "some missing data (specified start date was too early)");
            }
            else
            {
                TempEventMessage = String.Format("Weather data (GTA) polling completed with {0}.", "failure (specified end date was too early)");
            }
            args2.eventMessage = TempEventMessage;
            args2.expectedDuration = Convert.ToDouble(1.0);
            args2.progressBarPercent = 95.0;
            args2.timeReached = DateTime.Now;
            OnTaskUpdate(args);

            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error

        }
        #endregion

        #region DATABASE HANDLING METHODS - DBManager
        /// <summary>
        /// initialize the db connection for simulator - has option to backup the disk file db to mem for faster performance.
        /// </summary>
        public void OpenSimulatorDbConnection()
        {
            int numChangeBefore = DbConn_numChanges;
            if (!File.Exists(DbFile_Simulator) || StartNewGPSdbFiles)
            {
                SQLiteConnection.CreateFile(DbFile_Simulator);
            }
            SQLiteConnection source = new SQLiteConnection(@"Data Source=" + DbFile_Simulator + "; Cache Size=10000; Page Size=4096");//database on disk file
            source.Open();

            //option to load to memory or read from disk
            if (LoadDatabaseToMemory)
            {
                DbConn_Simulator = new SQLiteConnection(@"Data Source=:memory:");//database on mem
                DbConn_Simulator.Open();//keep db on mem open until it needs to be closed at end of session.

                //copy db file to memory - need to save changes if not read only
                source.BackupDatabase(DbConn_Simulator, "main", "main", -1, null, 0);
                source.Close();
            }
            else
            {
                //dbConn_Simulator = source;
                DbConn_Simulator = source;//new SQLiteConnection(@"Data Source=" + dbFile_Simulator + ".working" + "; Cache Size=10000; Page Size=4096");//database on disk file - dbFile_Simulator + ".working"
                                          //dbConn_Simulator.Open();//keep db on mem open until it needs to be closed at end of session.
                                          ////copy db file to memory - need to save changes if not read only
                                          //source.BackupDatabase(dbConn_Simulator, "main", "main", -1, null, 0);
                                          //source.Close();
            }

            //setup new DB Tables - empty GPS data and calculated GPS properties
            //GPSID, GPStime, UTCTime, VehID, Longitude, Latitude, RouteCode, Direction, RouteTag, Heading, TripCode, PrevStop, NextStop, DistFromShapeStart, DistToNextStop, EstiAvgSpeed, serviceID, TripID
            string sql_gps = "CREATE TABLE IF NOT EXISTS TTCGPS (GPSID INT not null, GPStime INT not null, UTCTime TEXT, VehID INT not null, Longitude REAL not null, Latitude REAL not null, RouteCode TEXT not null, Direction INT not null, RouteTag TEXT not null, Heading INT not null, TripCode TEXT not null, PrevStop INT, NextStop INT, DistFromShapeStart REAL, DistToNextStop REAL, EstiAvgSpeed REAL, serviceID INT, TripID INT, PRIMARY KEY (GPSID) ON CONFLICT REPLACE, UNIQUE (GPStime, VehID, Direction) ON CONFLICT REPLACE)";//combinations of selected columns must be unique - note, if multiple locations of the same vehicle going the same Direction is reported for the same GPStime, only the first one will be taken by the database. (, Longitude, Latitude), deleted: GtfsScheduleID INT, BearingStopA REAL, BearingStopB REAL, 
            SQLiteCommand command_gps = new SQLiteCommand(sql_gps, DbConn_Simulator);
            DbConn_numChanges += command_gps.ExecuteNonQuery();

            //setup new DB Tables - empty GPS trip data
            string sql_trips = "CREATE TABLE IF NOT EXISTS TTCGPSTRIPS (TripID INT not null, TripCode TEXT not null, RouteCode TEXT not null, Direction INT not null, RouteTag TEXT not null, GPSIDs TEXT not null, startGPSTime INT not null, GtfsScheduleID INT not null, tripStopIDs TEXT not null, tripStopDistances TEXT not null, tripStopArrTimes TEXT not null, tripDwellTimes TEXT not null, tripEstiDelays TEXT not null, tripEstiHeadways TEXT not null, tripScheduleHeadways TEXT not null, tripPrevTripIDs TEXT not null, gpsIDShapedistTime_ByStopID BLOB, PRIMARY KEY (TripID) ON CONFLICT REPLACE, UNIQUE (TripCode, GPSIDs) ON CONFLICT REPLACE)";
            SQLiteCommand command_trips = new SQLiteCommand(sql_trips, DbConn_Simulator);
            DbConn_numChanges += command_trips.ExecuteNonQuery();

            //setup new DB Tables - empty link object table for model
            string sql_linkObjects = "CREATE TABLE IF NOT EXISTS TTCLINKOBJ (LINKID INT not null, GtfsScheduleID INT not null, StartStopID INT not null, EndStopID INT not null, LinkDist REAL not null, IntxnIDsAll TEXT not null, OtherLinkVars TEXT not null, OtherRouteVars TEXT not null, PRIMARY KEY (LINKID) ON CONFLICT REPLACE, UNIQUE (GtfsScheduleID, StartStopID, EndStopID) ON CONFLICT REPLACE)";
            SQLiteCommand command_linkObjects = new SQLiteCommand(sql_linkObjects, DbConn_Simulator);
            DbConn_numChanges += command_linkObjects.ExecuteNonQuery();

            //setup new DB Tables - empty link data table for model
            string sql_linkData = "CREATE TABLE IF NOT EXISTS TTCLINKDATA (LINKDATAID INT not null, TrainOrTestData INT not null, LINKID INT not null, GPSTimeAtLinkStart INT not null, RunningTime REAL not null, StartStopDwellTime REAL not null, EndStopDwellTime REAL not null, DelayAtStart REAL not null, HeadwayAtStart REAL not null, WeatherSQID INT not null, IncidentSQID INT not null, TripID INT not null, PRIMARY KEY (LINKDATAID) ON CONFLICT REPLACE, UNIQUE (LINKDATAID) ON CONFLICT REPLACE)";
            SQLiteCommand command_linkData = new SQLiteCommand(sql_linkData, DbConn_Simulator);
            DbConn_numChanges += command_linkData.ExecuteNonQuery();

            //setup new DB Tables - empty table with stop location type for model
            string sql_stopObjects = "CREATE TABLE IF NOT EXISTS TTCLINK_STOPDATA (STOPID INT not null, StopLocTyp String not null, PRIMARY KEY (STOPID) ON CONFLICT REPLACE, UNIQUE (STOPID) ON CONFLICT REPLACE)";
            SQLiteCommand command_stopLocTyp = new SQLiteCommand(sql_stopObjects, DbConn_Simulator);
            DbConn_numChanges += command_stopLocTyp.ExecuteNonQuery();

            //create Incident Data Table, if it doesn't already exist
            //SQLiteConnection dbConn_Sim = new SQLiteConnection("Data Source=" + dbFile_Simulator + "; Cache Size=10000; Page Size=4096");//happens here either way
            //dbConn_Sim.Open();
            //setup new DB Tables - empty GPS data and calculated GPS properties
            string sql_inci = "CREATE TABLE IF NOT EXISTS TORINC (SQID INT not null, IncidentID TEXT not null, RoadName TEXT, NameOfLocation TEXT, DistrictType TEXT, Longitude REAL not null, Latitude REAL not null, RoadClass TEXT, Planned TEXT, SeverityOverride TEXT, LastUpdatedTime INT, StartTime INT, EndTime INT, WorkPeriod TEXT, Expired TEXT, WorkEventCause TEXT, PermitType TEXT, ContractorNameInvolved TEXT, ClosureDescription TEXT, PRIMARY KEY (SQID) ON CONFLICT REPLACE, UNIQUE (IncidentID) ON CONFLICT REPLACE)";//combinations of selected columns must be unique - note, if multiple locations of the same vehicle going the same Direction is reported for the same GPStime, only the first one will be taken by the database. (, Longitude, Latitude), deleted: GtfsScheduleID INT,
            SQLiteCommand command_inci = new SQLiteCommand(sql_inci, DbConn_Simulator);
            DbConn_numChanges += command_inci.ExecuteNonQuery();
            //dbConn_Sim.Close();

            //create Incident Data Table, if it doesn't already exist
            //SQLiteConnection dbConn_Sim = new SQLiteConnection("Data Source=" + dbFile_Simulator + "; Cache Size=10000; Page Size=4096");//happens here either way
            //dbConn_Sim.Open();
            //setup new DB Tables - empty GPS data and calculated GPS properties
            string sql_weather = "CREATE TABLE IF NOT EXISTS HISTWEA (SQID INT not null, WeatherStationID INT not null, WeatherTime INT not null, Longitude REAL not null, Latitude REAL not null, WeatherCondition TEXT, Temp REAL, Humidity REAL, WindSpeed REAL, WeatherStationName TEXT, RainPptnThreeHr REAL, SnowPptnThreeHr REAL, PRIMARY KEY (SQID) ON CONFLICT REPLACE, UNIQUE (WeatherStationID, WeatherTime) ON CONFLICT REPLACE)";//combinations of selected columns must be unique - note, if multiple locations of the same vehicle going the same Direction is reported for the same GPStime, only the first one will be taken by the database. (, Longitude, Latitude), deleted: GtfsScheduleID INT,
            SQLiteCommand command_weather = new SQLiteCommand(sql_weather, DbConn_Simulator);
            DbConn_numChanges += command_weather.ExecuteNonQuery();

            ////setup new DB Tables - empty GPS link data
            //string sql3 = "CREATE TABLE TTCGPSLINKS (RouteCode INT not null, StartStopId INT not null, EndStopId INT not null, IntermediateStopIDs TEXT not null, GPSIDs TEXT not null, PRIMARY KEY (StartStopId,EndStopId) ON CONFLICT REPLACE, UNIQUE (GPSIDs))";
            //SQLiteCommand command3 = new SQLiteCommand(sql3, dbConn_GPS);
            //command3.ExecuteNonQuery();
            if (DbConn_numChanges == numChangeBefore)//all commands to create table had been successfully carried out - save empty tables
            {
                SaveSimulatorDbChanges(false);
                DbConn_numChanges = 0;//reset count
            }
        }
        /// <summary>
        /// save any changes from the working db on mem/disk to the permanent disk file db.
        /// </summary>
        public void SaveSimulatorDbChanges(bool vacuum)
        {
            //vacuum to get rid of deleted data
            if (vacuum && DeleteActionTracker > 0)
            {
                string sql = string.Format("vacuum");
                using (SQLiteCommand cmd = new SQLiteCommand(sql, DbConn_Simulator))
                {
                    cmd.CommandType = CommandType.Text;
                    int rowsAffected = cmd.ExecuteNonQuery();
                    //if (rowsAffected == 0)
                    //{
                    //    GPSTripGlobalID--;
                    //}
                }
            }

            //option to load to memory or read from disk
            if (LoadDatabaseToMemory)
            {
                // save memory db to file - optionally, only do if write has been performed
                SQLiteConnection source = new SQLiteConnection("Data Source=" + DbFile_Simulator + "; Cache Size=10000; Page Size=4096");
                source.Open();
                DbConn_Simulator.BackupDatabase(source, "main", "main", -1, null, 0);
                source.Close();
            }
            else
            {
                //// save memory db to file - optionally, only do if write has been performed
                //SQLiteConnection source = new SQLiteConnection("Data Source=" + dbFile_Simulator + "; Cache Size=10000; Page Size=4096");
                //source.Open();
                //dbConn_Simulator.BackupDatabase(source, "main", "main", -1, null, 0);
                //source.Close();
            }
        }
        /// <summary>
        /// save and then close the db connection
        /// </summary>
        public void CloseSimulatorDbConnection(bool hasChangesToBeSaved = true)
        {
            SaveSimulatorDbChanges(hasChangesToBeSaved);
            DbConn_Simulator.Close();//close the db on working disk connection
            DbConn_Simulator.Dispose();//disposing the connection to allow working file deletion

            //if (File.Exists(dbFile_Simulator + ".working"))
            //{
            //    File.Delete(dbFile_Simulator + ".working");//delete the working file of the database
            //}
        }
        
        //Log writing for Diagnostic - can be disabled in Simulator settings
        private StringBuilder logString;
        private void WriteLog(string currentStrings, bool writeToFile)
        {
            if (WriteToLog)
            {
                string logFileName = "SSimulatorLogs";
                if (writeToFile)
                {
                    logString.Append(currentStrings + "\n");
                    if (!Directory.Exists(LogFileFolder))
                    {
                        Directory.CreateDirectory(LogFileFolder);
                    }
                    using (System.IO.StreamWriter file =
                        new System.IO.StreamWriter(LogFileFolder + logFileName + @".txt", true))
                    {
                        file.Write(logString.ToString());
                    }
                    logString.Clear();
                }
                else
                {
                    logString.Append(currentStrings + "\n");
                }
            }
        }
        /// <summary>
        /// Update GUI: progressbar and messages
        /// </summary>
        /// <param name="statusMsg"></param>
        /// <param name="progressBarPercent"></param>
        /// <param name="secondsPerHrOfDataForNetwork"></param>
        /// <param name="secondsElapse"></param>
        private void UpdateGUI_StatusBox(string statusMsg, double progressBarPercent, double secondsPerHrOfDataForNetwork = 0.0, double secondsElapse = 0.0)
        {
            double duration = 0.0;
            if (secondsPerHrOfDataForNetwork != 0.0)
            {
                double durationInHrOfData = 0.0;
                foreach (DateRange aDateRange in TrainingDateRanges)
                {
                    durationInHrOfData += (aDateRange.end.DateTimeToEpochTime() - aDateRange.start.DateTimeToEpochTime()) / 3600;
                }
                foreach (DateRange aDateRange in TestSetDateRanges)
                {
                    durationInHrOfData += (aDateRange.end.DateTimeToEpochTime() - aDateRange.start.DateTimeToEpochTime()) / 3600;
                }
                duration = (secondsPerHrOfDataForNetwork) * durationInHrOfData;// / 180 * routeTagList.Count;//approximate run time in s
            }
            else if (secondsElapse != 0.0)
            {
                duration = secondsElapse;
            }
            TaskUpdateEventArgs args = new TaskUpdateEventArgs();
            args.eventMessage = statusMsg;
            args.expectedDuration = duration;
            args.progressBarPercent = progressBarPercent;
            args.timeReached = DateTime.Now;
            OnTaskUpdate(args);
        }
        private void UpdateGUI_LogBox(string logMsg)
        {
            LogUpdateEventArgs args = new LogUpdateEventArgs();
            args.logMessage = logMsg;
            OnLogUpdate(args);
        }

        public void SetLogSetting(bool writeLog)
        {
            WriteToLog = writeLog;
        }

        public void SetDataValidation(bool validateLinkTTModel)
        {
            ValidateModel = validateLinkTTModel;
        }

        /// <summary>
        /// true for the use of nextBus data, false for use of CVST
        /// </summary>
        /// <param name="isNextBusData"></param>
        public void SetGPSDataSource(bool isNextBusData)
        {
            UseNextBusGPSData = isNextBusData;
        }

        /// <summary>
        /// close database and output log string
        /// </summary>
        public void CloseDatabase()
        {
            //handles any unwritten logs
            WriteLog("", true);
            CloseSimulatorDbConnection(true);
        }

        private int GetNextGPSTripIDForTable()
        {
            if (GpsTripCurrentMaxID <= 0)
            {
                GpsTripCurrentMaxID = 1;
            }
            while (GpsTripTable.ContainsKey(GpsTripCurrentMaxID))
            {
                GpsTripCurrentMaxID++;
            }
            return GpsTripCurrentMaxID;
        }
        private int GetNextGPSIDForTable()
        {
            if (GpsPointCurrentMaxID <= 0)
            {
                GpsPointCurrentMaxID = 1;
            }
            while (GpsPointTable.ContainsKey(GpsPointCurrentMaxID))
            {
                GpsPointCurrentMaxID++;
            }
            return GpsPointCurrentMaxID;
        }

        #endregion

        #region EVENT HANDLING METHODS
        // Event Handling for updating task progress
        public event EventHandler<TaskUpdateEventArgs> TaskUpdate;
        //public event EventHandler<TaskUpdateEventArgs> TaskUpdate;
        protected virtual void OnTaskUpdate(TaskUpdateEventArgs e)
        {
            EventHandler<TaskUpdateEventArgs> handler = TaskUpdate;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        // Event Handling for updating task progress
        public event EventHandler<LogUpdateEventArgs> LogUpdate;
        //public event EventHandler<TaskUpdateEventArgs> TaskUpdate;
        protected virtual void OnLogUpdate(LogUpdateEventArgs e)
        {
            EventHandler<LogUpdateEventArgs> handler = LogUpdate;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        #endregion
    }//end class

    #region EVENT HANDLING CLASS
    public class TaskUpdateEventArgs : EventArgs
    {
        public double progressBarPercent { get; set; }//value to update main UI progress bar
        public double expectedDuration { get; set; }//expected duration of the event in seconds
        public string eventMessage { get; set; }
        public DateTime timeReached { get; set; }
    }
    public delegate void TaskUpdateEventHandler(Object sender, TaskUpdateEventArgs e);
    public class LogUpdateEventArgs : EventArgs
    {
        public string logMessage { get; set; }
    }
    public delegate void LogUpdateEventHandler(Object sender, LogUpdateEventArgs e);
    #endregion
}