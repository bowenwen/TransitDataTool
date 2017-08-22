using System;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.IO;
using System.Text;
using MainClass;
using System.Linq;
using System.Xml;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MainClass.DataTools
{

    //http://webservices.nextbus.com/service/publicXMLFeed?command=vehicleLocations&a=ttc&r=501&t=1476913553076
    //https://msdn.microsoft.com/en-us/library/system.xml.serialization.xmlserializer(v=vs.110).aspx
    //http://xmltocsharp.azurewebsites.net/

    public class SSWebCrawlerNextBus
    {
        string xmlGPSFolderPath;
        string xmlGPSFilenameFormat;
        long fileSaveFreq; //time interval to save the permanent xml file - HOURLY = 3600
        long bkSaveFreq; //xml raw file backup period - raw xml files saved will be deleted after this time interval. = 60
        private long lasttimeTimeURLField;

        List<SSVehGPSDataTable> processedGPSDataFromXML;

        public SSWebCrawlerNextBus(string xmlFolderPath, long xmlFileTimeIncreInSecs, long xmlFileBackupTimeInSecs)
        {
            xmlGPSFolderPath = xmlFolderPath;
            fileSaveFreq = xmlFileTimeIncreInSecs;//how often files are saved
            bkSaveFreq = xmlFileBackupTimeInSecs;//how often backup files are saved
            processedGPSDataFromXML = new List<SSVehGPSDataTable>();
            xmlGPSFilenameFormat = "all-GPS-{0}-{1}.xml";
        }

        /// <summary>
        /// Polling frequency should be equal or less than 20s for max polling rate
        /// </summary>
        /// <param name="pollingFrequency"></param>
        /// <param name="durationInSecs"></param>
        /// <returns></returns>
        private ConcurrentDictionary<Tuple<long, string>, Vehicle> downloadedGPSXMLData_UNSAVED;
        public int startLiveDownloadTask(DateTime pollingStart, DateTime pollingEnd, double pollingFrequency)
        {
            lasttimeTimeURLField = 0;
            int downloadState = 0;
            //NOTE: polling freq: 19s < bk freq: 60s < save freq: 3600s
            downloadedGPSXMLData_UNSAVED = new ConcurrentDictionary<Tuple<long, string>, Vehicle>();//GPSTime & VehID as tuple index
            DateTime nextDownloadTime;// = DateTime.Now.ToLocalTime().AddSeconds(pollingFrequency);
            DateTime nextBackupTime;// = DateTime.Now.ToLocalTime().AddSeconds(bkSaveFreq);
            //DateTime nextSaveTime = DateTime.Now.ToLocalTime().AddSeconds(fileSaveFreq);
            DateTime nextSaveTime;// = pollingStart.Date.AddSeconds(pollingStart.Hour * 3600 - 1 + fileSaveFreq);//save on the last second of the next hour
            long lastLogUpdatedSave = 0;
            string bkfilename = "current-GPSXML-bk.xml";

            ////load last saved backup, if it is from within the last 5 minutes
            //if ((DateTime.Now.DateTimeToEpochTime() - File.GetLastWriteTime(bkfilename).DateTimeToEpochTime()) < (5 * 60))
            //{
            //    List<Vehicle> bkGPSPoints = new List<Vehicle>();
            //    bkGPSPoints = DeSerializeXMLObject<List<Vehicle>>(xmlGPSFolderPath + bkfilename);
            //    foreach (Vehicle aGPSPoint in bkGPSPoints)
            //    {
            //        downloadedGPSXMLData_UNSAVED.AddOrUpdate(new Tuple<long, string>(aGPSPoint.GPStime, aGPSPoint.Id), aGPSPoint, (k, v) => aGPSPoint);//replaces existing if it exists
            //    }
            //}

            if (pollingStart.DateTimeToEpochTime() >= DateTime.Now.ToLocalTime().DateTimeToEpochTime())//future download - put thread to sleep until start - proper state
            {
                //return pollingStart.DateTimeToEpochTime() - downloadStartTime.DateTimeToEpochTime();
                Thread.Sleep(Convert.ToInt32(1000 * (pollingStart.DateTimeToEpochTime() - DateTime.Now.ToLocalTime().DateTimeToEpochTime() - 1)));
                nextDownloadTime = pollingStart.AddSeconds(0);
                nextBackupTime = pollingStart.AddSeconds(0);
                nextSaveTime = pollingStart.Date.AddSeconds(pollingStart.Hour * 3600 - 1 + fileSaveFreq);//assume file save frequency is at the hour
            }
            else if ((pollingStart.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime()) && (pollingEnd.DateTimeToEpochTime() > DateTime.Now.ToLocalTime().DateTimeToEpochTime()))//some missing downloads - start period is before download can take place
            {
                //polling reschedule message
                LogUpdateEventArgs args = new LogUpdateEventArgs();
                args.logMessage = String.Format("NextBus Polling to Start at: {0}.", DateTime.Now.ToLocalTime().Date.AddSeconds(DateTime.Now.ToLocalTime().Hour * 3600 + fileSaveFreq).DateTimeISO8601Format());
                OnLogUpdate(args);

                Thread.Sleep(Convert.ToInt32(1000 * (3600 - (DateTime.Now.ToLocalTime().Minute * 60 + DateTime.Now.ToLocalTime().Second - 1))));
                downloadState = -1;
                nextDownloadTime = DateTime.Now.ToLocalTime();
                nextBackupTime = DateTime.Now.ToLocalTime();
                nextSaveTime = DateTime.Now.ToLocalTime().Date.AddSeconds(DateTime.Now.ToLocalTime().Hour * 3600 - 1 + fileSaveFreq);//assume file save frequency is at the hour
            }
            else //if (pollingEnd.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())//nothing to download - end period is before download can take place
            {
                nextDownloadTime = new DateTime();
                nextBackupTime = new DateTime();
                nextSaveTime = new DateTime();
                downloadState = -2;
                return downloadState;
            }

            //initial save message
            if (lastLogUpdatedSave != nextSaveTime.DateTimeToEpochTime())
            {
                lastLogUpdatedSave = nextSaveTime.DateTimeToEpochTime();
                LogUpdateEventArgs args = new LogUpdateEventArgs();
                args.logMessage = String.Format("NextBus Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
                OnLogUpdate(args);
            }

            //download started after a proper wait - proper state, or start immediate if some data can be downloaded (state: -1)
            while (pollingEnd.DateTimeToEpochTime() >= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
            {
                List<long> nextOpTime = new List<long>();
                nextOpTime.Add(nextDownloadTime.DateTimeToEpochTime());
                nextOpTime.Add(nextBackupTime.DateTimeToEpochTime());
                nextOpTime.Add(nextSaveTime.DateTimeToEpochTime());
                nextOpTime.Sort();
                if ((nextOpTime[0] > DateTime.Now.ToLocalTime().DateTimeToEpochTime()))
                {
                    if (lastLogUpdatedSave != nextSaveTime.DateTimeToEpochTime())
                    {
                        lastLogUpdatedSave = nextSaveTime.DateTimeToEpochTime();
                        LogUpdateEventArgs args = new LogUpdateEventArgs();
                        args.logMessage = String.Format("NextBus Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
                        OnLogUpdate(args);
                    }

                    long sleepTime = nextOpTime[0] - DateTime.Now.ToLocalTime().DateTimeToEpochTime();
                    Thread.Sleep(Convert.ToInt32(1000 * sleepTime));//brief sleep in between operations
                }
                //this condition structure prioritize download to mem, then backup, then save
                if (nextDownloadTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextDownloadTime = nextDownloadTime.AddSeconds(pollingFrequency);
                    downloadCurrentGPSXMLFromWeb(pollingFrequency);
                    //List<Vehicle> newGPSPoints = downloadCurrentGPSXMLFromWeb(pollingFrequency);
                    //foreach (Vehicle aGPSPoint in newGPSPoints)
                    //{
                    //    downloadedGPSXMLData_UNSAVED.AddOrUpdate(new Tuple<long, string>(aGPSPoint.GPStime, aGPSPoint.Id), aGPSPoint, (k, v) => aGPSPoint);//replaces existing if it exists
                    //}
                }
                else if (nextBackupTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextBackupTime = nextBackupTime.AddSeconds(bkSaveFreq);
                    SSUtil.SerializeXMLObject(downloadedGPSXMLData_UNSAVED.Values.ToList(), xmlGPSFolderPath + bkfilename);
                }
                else if (nextSaveTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    if (downloadedGPSXMLData_UNSAVED.Values.Count > 0)//avoid saving empty file
                    {
                        string newfilename = string.Format(xmlGPSFilenameFormat, nextSaveTime.AddSeconds(-fileSaveFreq + 1).ToUniversalTime().DateTimeNamingFormat(), nextSaveTime.ToUniversalTime().DateTimeNamingFormat());
                        SSUtil.SerializeXMLObject(downloadedGPSXMLData_UNSAVED.Values.ToList(), xmlGPSFolderPath + newfilename);
                        downloadedGPSXMLData_UNSAVED.Clear();//clear mem
                    }
                    nextSaveTime = nextSaveTime.AddSeconds(fileSaveFreq);
                }
            }
            return downloadState;
            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error
        }
        /// <summary>
        /// More info: https://www.nextbus.com/xmlFeedDocs/NextBusXMLFeed.pdf
        /// </summary>
        /// <param name="pollingOrRequestFrequency"></param>
        private async void downloadCurrentGPSXMLFromWeb(double pollingOrRequestFrequency)//download current GPS XML data from NextBus with processing for GPSTime
        {
            try
            {
                LastTime dataDownloadTime = new LastTime();
                List<Vehicle> downloadedGPSXML = new List<Vehicle>();
                Uri uriToDownload = new Uri(String.Format("http://webservices.nextbus.com/service/publicXMLFeed?command=vehicleLocations&a={0}&t={1}", "ttc", lasttimeTimeURLField));
                using (WebClient client = new WebClient())
                {
                    //download string and write to file
                    string xml = await client.DownloadStringTaskAsync(uriToDownload);
                    string xmlString = Convert.ToString(xml);
                    using (StringReader reader = new StringReader(xmlString))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(GPSXMLQueryResult));
                        //XmlSerializer vehSerializer = new XmlSerializer(typeof(Vehicle));
                        //XmlSerializer lasttimeSerializer = new XmlSerializer(typeof(LastTime));
                        string headerString = reader.ReadLine();
                        string currentVehString = reader.ReadToEnd();
                        MemoryStream memStream = new MemoryStream(Encoding.UTF8.GetBytes(currentVehString));
                        GPSXMLQueryResult currentXMLGPSPoints = (GPSXMLQueryResult)serializer.Deserialize(memStream);
                        dataDownloadTime = currentXMLGPSPoints.LastTime;
                        downloadedGPSXML = currentXMLGPSPoints.Vehicles;
                    }//end using reader
                }//end using cilent

                long currentLasttimeTimeField = Convert.ToInt64(dataDownloadTime.Time);
                //find GPSTime based on objects received
                if (!dataDownloadTime.Time.Equals(null) && !(downloadedGPSXML.Count == 0))
                {
                    long currentDownloadTime = currentLasttimeTimeField / 1000;//in seconds
                    for (int i = 0; i < downloadedGPSXML.Count; i++)
                    {
                        downloadedGPSXML[i].GPStime = currentDownloadTime - Convert.ToInt64(downloadedGPSXML[i].SecsSinceReport);
                    }
                }
                //Add downloadedGPSXML to downloadedGPSXMLData_UNSAVED
                foreach (Vehicle aGPSPoint in downloadedGPSXML)
                {
                    downloadedGPSXMLData_UNSAVED.AddOrUpdate(new Tuple<long, string>(aGPSPoint.GPStime, aGPSPoint.Id), aGPSPoint, (k, v) => aGPSPoint);//replaces existing if it exists
                }
                lasttimeTimeURLField = Convert.ToInt64(dataDownloadTime.Time);//in msec
            }
            catch (Exception e)
            {

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="RouteTags"></param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <returns></returns>
        public List<SSVehGPSDataTable> RetriveGPSData(List<string> RouteTags, DateTime startTime, DateTime endTime)
        {
            List<SSVehGPSDataTable> finalPackageOfGPSDataAllRoutes = new List<SSVehGPSDataTable>();
            //List<SSVehGPSDataTable> allDataInTheGPSDataFile = new List<SSVehGPSDataTable>();
            Dictionary<string, List<SSVehGPSDataTable>> allDataInGPSDataFile = new Dictionary<string, List<SSVehGPSDataTable>>();
            List<string> gpsDataFilenames = new List<string>();//in order

            DateTime lastIntermediateTime = startTime;//intermediate time 59:59 (min:sec) from start
            DateTime intermediateTime = startTime.Date.AddHours(startTime.Hour).AddSeconds(fileSaveFreq - 1);//intermediate time 59:59 (min:sec) from start
            //initiate filenames
            while (intermediateTime.DateTimeToEpochTime() <= endTime.DateTimeToEpochTime())
            {
                string currentfilename = string.Format(xmlGPSFilenameFormat, lastIntermediateTime.ToUniversalTime().DateTimeNamingFormat(), intermediateTime.ToUniversalTime().DateTimeNamingFormat());
                gpsDataFilenames.Add(currentfilename);
                //increment to read next file
                lastIntermediateTime = lastIntermediateTime.AddSeconds(fileSaveFreq);
                intermediateTime = intermediateTime.AddSeconds(fileSaveFreq);
            }

            //load all files in parallel
            ParallelOptions pOptions = new ParallelOptions();
            pOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;
            Parallel.For(0, gpsDataFilenames.Count, pOptions, i =>
            {
                string currentfilename = gpsDataFilenames[i];
                allDataInGPSDataFile.Add(currentfilename, (convertRawGPSToSSFormat(SSUtil.DeSerializeXMLObject<List<Vehicle>>(xmlGPSFolderPath + currentfilename))));
            });
            //add each file, then each gps point, in order
            foreach (string currentfilename in gpsDataFilenames)
            {
                //filter by routeTag
                foreach (SSVehGPSDataTable aGPSPoint in allDataInGPSDataFile[currentfilename])
                {
                    string aGPSPointRouteTag = aGPSPoint.TripCode == "NULL" ? "" : aGPSPoint.TripCode.Split('_')[0];
                    if (RouteTags.Contains(aGPSPointRouteTag))
                    {
                        finalPackageOfGPSDataAllRoutes.Add(aGPSPoint);
                    }
                }
                //List<SSVehGPSDataTable> filteredData = allDataInGPSDataFile[currentfilename].Where(v => RouteTags.Contains(v.TripCode.Split('_')[0])).ToList();
                //finalPackageOfGPSDataAllRoutes.AddRange(filteredData);
            }

            allDataInGPSDataFile.Clear();
            gpsDataFilenames.Clear();
            return finalPackageOfGPSDataAllRoutes;//return result list
        }
        private List<SSVehGPSDataTable> convertRawGPSToSSFormat(List<Vehicle> downloadedGPSData)
        {
            List<SSVehGPSDataTable> convertedData = new List<SSVehGPSDataTable>();

            foreach (Vehicle aRawGPS in downloadedGPSData)
            {
                if (aRawGPS.DirTag != null && aRawGPS.DirTag != "NULL")
                {
                    SSVehGPSDataTable aConvertedData = new SSVehGPSDataTable();

                    string routeNum = aRawGPS.RouteTag.Length > 0 ? aRawGPS.RouteTag : "-1";
                    string dir = aRawGPS.DirTag == "NULL" ? "-1" : aRawGPS.DirTag.Split('_')[1];
                    string RouteTag = aRawGPS.DirTag == "NULL" ? "-1" : aRawGPS.DirTag.Split('_')[2];
                    string TripCode = string.Concat(routeNum, "_", RouteTag, "_", aRawGPS.Id);

                    aConvertedData.gpsID = -1;//unassigned
                    aConvertedData.GPStime = aRawGPS.GPStime;
                    aConvertedData.vehID = Convert.ToInt32(aRawGPS.Id);
                    aConvertedData.TripCode = TripCode;
                    aConvertedData.Direction = Convert.ToInt32(dir);
                    aConvertedData.Longitude = Convert.ToDouble(aRawGPS.Lon);
                    aConvertedData.Latitude = Convert.ToDouble(aRawGPS.Lat);
                    aConvertedData.GPStime = aRawGPS.GPStime;
                    aConvertedData.Heading = Convert.ToInt32(aRawGPS.Heading);

                    convertedData.Add(aConvertedData);
                }
            }

            return convertedData;
        }

        #region EVENT HANDLING METHODS
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
    }

    #region XML OBJECT CLASSES
    [XmlRoot(ElementName = "vehicle")]
    public class Vehicle
    {
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "routeTag")]
        public string RouteTag { get; set; }
        [XmlAttribute(AttributeName = "dirTag")]
        public string DirTag { get; set; }
        [XmlAttribute(AttributeName = "lat")]
        public string Lat { get; set; }
        [XmlAttribute(AttributeName = "lon")]
        public string Lon { get; set; }
        [XmlAttribute(AttributeName = "secsSinceReport")]
        public string SecsSinceReport { get; set; }
        [XmlAttribute(AttributeName = "predictable")]
        public string Predictable { get; set; }
        [XmlAttribute(AttributeName = "heading")]
        public string Heading { get; set; }
        public long GPStime { get; set; }//calculated GPStime based on last time and SecsSinceReport
    }

    [XmlRoot(ElementName = "lastTime")]
    public class LastTime
    {
        [XmlAttribute(AttributeName = "time")]
        public string Time { get; set; }
    }

    [XmlRoot(ElementName = "body")]
    public class GPSXMLQueryResult
    {
        [XmlElement(ElementName = "vehicle")]
        public List<Vehicle> Vehicles { get; set; }
        [XmlElement(ElementName = "lastTime")]
        public LastTime LastTime { get; set; }
        [XmlAttribute(AttributeName = "copyright")]
        public string Copyright { get; set; }
    }
    #endregion

    #region EVENT HANDLING CLASS
    //public class LogUpdateEventArgs : EventArgs
    //{
    //    public string logMessage { get; set; }
    //}
    //public delegate void LogUpdateEventHandler(Object sender, LogUpdateEventArgs e);
    #endregion
}
