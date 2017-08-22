using System;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Text;
using System.Net;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;

namespace MainClass.DataTools
{
    public class SSWebCrawlerTORdRestr
    {
        string xmlINCIFolderPath;
        string xmlINCIFilenameFormat; //save name datetime in UTC - avoid DST and EST confusion
        long fileSaveFreq; //time interval to save the permanent xml file - DAILY = 3600 * 24
        long bkSaveFreq; //xml raw file backup period - raw xml files saved will be deleted after this time interval. = 2 * 60

        List<SSIncidentDataTable> processedINCIDataFromXML;

        public SSWebCrawlerTORdRestr(string xmlFolderPath, long xmlFileBackupTimeInSecs)//, long xmlFileTimeIncreInSecs
        {
            xmlINCIFolderPath = xmlFolderPath;
            fileSaveFreq = 3600 * 24;//how often files are saved
            bkSaveFreq = xmlFileBackupTimeInSecs;//how often backup files are saved
            processedINCIDataFromXML = new List<SSIncidentDataTable>();
            xmlINCIFilenameFormat = "all-RR-{0}-{1}.xml";
        }

        /// <summary>
        /// Polling frequency should be equal or less than 20s for max polling rate
        /// </summary>
        /// <param name="pollingFrequency"></param>
        /// <param name="durationInSecs"></param>
        /// <returns></returns>
        private ConcurrentDictionary<string, Closure> downloadedINCIXMLData_UNSAVED;
        public int startLiveDownloadTask(DateTime pollingStart, DateTime pollingEnd, double pollingFrequency)
        {
            //fileSaveFreq must be 1 day, otherwise, needs to modify nextSaveTime
            int downloadState = 0;
            //NOTE: polling freq: 60s < bk freq: 120s < save freq: 3600s * 24 = 1 day
            downloadedINCIXMLData_UNSAVED = new ConcurrentDictionary<string, Closure>();//INCITime & VehID as tuple index
            DateTime nextDownloadTime;// = DateTime.Now.ToLocalTime().AddSeconds(pollingFrequency);
            DateTime nextBackupTime;// = DateTime.Now.ToLocalTime().AddSeconds(bkSaveFreq);
            //DateTime nextSaveTime = DateTime.Now.ToLocalTime().AddSeconds(fileSaveFreq);
            DateTime nextSaveTime;// = pollingStart.Date.AddSeconds(pollingStart.Hour * 3600 - 1 + fileSaveFreq);//save on the last second of the next hour
            long lastLogUpdatedSave = 0;
            string bkfilename = "current-RRXML-bk.xml";

            //load last saved backup, if it is from within the last 5 minutes
            if ((DateTime.Now.DateTimeToEpochTime() - File.GetLastWriteTime(bkfilename).DateTimeToEpochTime()) < (5 * 60))
            {
                List<Closure> bkINCIPoints = new List<Closure>();
                bkINCIPoints = SSUtil.DeSerializeXMLObject<List<Closure>>(xmlINCIFolderPath + bkfilename);
                foreach (Closure aINCIPoint in bkINCIPoints)
                {
                    downloadedINCIXMLData_UNSAVED.AddOrUpdate(aINCIPoint.Id, aINCIPoint, (k, v) => aINCIPoint);//replaces existing if it exists
                }
            }

            if (pollingStart.DateTimeToEpochTime() >= DateTime.Now.ToLocalTime().DateTimeToEpochTime())//future download - put thread to sleep until start - proper state
            {
                //return pollingStart.DateTimeToEpochTime() - downloadStartTime.DateTimeToEpochTime();
                Thread.Sleep(Convert.ToInt32(1000 * (pollingStart.DateTimeToEpochTime() - DateTime.Now.ToLocalTime().DateTimeToEpochTime() - 1)));
                nextDownloadTime = pollingStart.AddSeconds(0);
                nextBackupTime = pollingStart.AddSeconds(0);
                nextSaveTime = DateTime.Now.ToLocalTime().Date.AddSeconds(-1 + fileSaveFreq);//asume save frequencies are in the scale of days, save at midnighht
            }
            else if ((pollingStart.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime()) && (pollingEnd.DateTimeToEpochTime() > DateTime.Now.ToLocalTime().DateTimeToEpochTime()))//some missing downloads - start period is before download can take place
            {
                //download at next hour, catch whatever data can be retrived.
                //Thread.Sleep(Convert.ToInt32(1000 * (3600 - (DateTime.Now.ToLocalTime().Minute * 60 + DateTime.Now.ToLocalTime().Second) - 1)));
                downloadState = -1;
                nextDownloadTime = DateTime.Now.ToLocalTime().AddSeconds(0);//no delay for download
                nextBackupTime = DateTime.Now.ToLocalTime().AddSeconds(0);//no delay for backup
                nextSaveTime = DateTime.Now.ToLocalTime().Date.AddSeconds(-1 + fileSaveFreq);//asume save frequencies are in the scale of days, save at midnighht
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
                args.logMessage = String.Format("Rd Closures Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
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
                        args.logMessage = String.Format("Rd Closures Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
                        OnLogUpdate(args);
                    }

                    long sleepTime = nextOpTime[0] - DateTime.Now.ToLocalTime().DateTimeToEpochTime();
                    Thread.Sleep(Convert.ToInt32(1000 * sleepTime));//brief sleep in between operations
                }
                //this condition structure prioritize download to mem, then backup, then save
                if (nextDownloadTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextDownloadTime = nextDownloadTime.AddSeconds(pollingFrequency);
                    downloadCurrentINCIXMLFromWeb(pollingFrequency);
                    //List < Closure > newINCIPoints = downloadCurrentINCIXMLFromWeb(pollingFrequency);
                    //foreach (Closure aINCIPoint in downloadedINCIXML)
                    //{
                    //    downloadedINCIXMLData_UNSAVED.AddOrUpdate(aINCIPoint.Id, aINCIPoint, (k, v) => aINCIPoint);//replaces existing if it exists
                    //}
                }
                else if (nextBackupTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextBackupTime = nextBackupTime.AddSeconds(bkSaveFreq);
                    SSUtil.SerializeXMLObject(downloadedINCIXMLData_UNSAVED.Values.ToList(), xmlINCIFolderPath + bkfilename);
                }
                else if (nextSaveTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    if (downloadedINCIXMLData_UNSAVED.Values.Count > 0)//avoid saving empty file
                    {
                        string newfilename = string.Format(xmlINCIFilenameFormat, nextSaveTime.AddSeconds(-fileSaveFreq + 1).ToUniversalTime().DateTimeNamingFormat(), nextSaveTime.ToUniversalTime().DateTimeNamingFormat());
                        SSUtil.SerializeXMLObject(downloadedINCIXMLData_UNSAVED.Values.ToList(), xmlINCIFolderPath + newfilename);
                        downloadedINCIXMLData_UNSAVED.Clear();//clear mem
                    }
                    nextSaveTime = nextSaveTime.AddSeconds(fileSaveFreq);
                }
            }
            return downloadState;
            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error
        }
        private async void downloadCurrentINCIXMLFromWeb(double pollingOrRequestFrequency)//download current INCI XML data from NextBus with processing for INCITime
        {
            try
            {
                List<Closure> downloadedINCIXML = new List<Closure>();
                Uri uriToDownload = new Uri("http://www1.toronto.ca/transportation/roadrestrictions/RoadRestrictions.xml");
                using (WebClient client = new WebClient())
                {
                    //download string and write to file
                    //string xml = client.DownloadString(uriToDownload);
                    string xml = await client.DownloadStringTaskAsync(uriToDownload);
                    string xmlString = Convert.ToString(xml);
                    using (StringReader reader = new StringReader(xmlString))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(Closures));
                        string headerString = reader.ReadLine();
                        string currentVehString = reader.ReadToEnd();
                        MemoryStream memStream = new MemoryStream(Encoding.UTF8.GetBytes(currentVehString));
                        Closures currentXMLINCIPoints = (Closures)serializer.Deserialize(memStream);
                        downloadedINCIXML.AddRange(currentXMLINCIPoints.Closure);
                    }//end using reader
                }//end using cilent

                //add downloadedINCIXML to downloadedINCIXMLData_UNSAVED
                foreach (Closure aINCIPoint in downloadedINCIXML)
                {
                    downloadedINCIXMLData_UNSAVED.AddOrUpdate(aINCIPoint.Id, aINCIPoint, (k, v) => aINCIPoint);//replaces existing if it exists
                }
            }
            catch (Exception e)
            {

            }
        }
        //private List<Closure> downloadCurrentINCIXMLFromWeb(double pollingOrRequestFrequency)//download current INCI XML data from NextBus with processing for INCITime
        //{
        //    List<Closure> downloadedINCIXML = new List<Closure>();
        //    Uri uriToDownload = new Uri("http://www1.toronto.ca/transportation/roadrestrictions/RoadRestrictions.xml");

        //    repeatWebDownload:
        //    try
        //    {
        //        using (WebClient client = new WebClient())
        //        {
        //            //download string and write to file
        //            string xml = client.DownloadString(uriToDownload);
        //            string xmlString = Convert.ToString(xml);
        //            using (StringReader reader = new StringReader(xmlString))
        //            {
        //                XmlSerializer serializer = new XmlSerializer(typeof(Closures));
        //                string headerString = reader.ReadLine();
        //                string currentVehString = reader.ReadToEnd();
        //                MemoryStream memStream = new MemoryStream(Encoding.UTF8.GetBytes(currentVehString));
        //                Closures currentXMLINCIPoints = (Closures)serializer.Deserialize(memStream);
        //                downloadedINCIXML.AddRange(currentXMLINCIPoints.Closure);
        //            }//end using reader
        //        }//end using cilent
        //    }
        //    catch (Exception e)
        //    {
        //        //sleep to aviod too many requests
        //        Thread.Sleep(Convert.ToInt32(1000 * pollingOrRequestFrequency / 2));//check delay at "half of" polling frequency (minute), until it is time for download
        //        goto repeatWebDownload;
        //    }

        //    return downloadedINCIXML;
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="RouteTags"></param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <returns></returns>
        public List<SSIncidentDataTable> RetriveINCIData(DateTime startTime, DateTime endTime, List<string> RouteTags = null)
        {
            List<SSIncidentDataTable> finalPackageOfINCIDataAllRoutes = new List<SSIncidentDataTable>();
            List<SSIncidentDataTable> allDataInTheINCIDataFile = new List<SSIncidentDataTable>();

            //For incident and weather data, saves are daily
            startTime = startTime.Date;//beginning of first day
            endTime = endTime.Date.AddDays(1).AddSeconds(-1);//end of second day

            DateTime lastIntermediateTime = startTime;//intermediate time 59:59 (min:sec) from start
            DateTime intermediateTime = startTime.Date.AddHours(startTime.Hour).AddSeconds(fileSaveFreq - 1);//intermediate time 59:59 (min:sec) from start

            while (intermediateTime.DateTimeToEpochTime() <= endTime.DateTimeToEpochTime())
            {
                string currentfilename = string.Format(xmlINCIFilenameFormat, lastIntermediateTime.ToUniversalTime().DateTimeNamingFormat(), intermediateTime.ToUniversalTime().DateTimeNamingFormat());
                allDataInTheINCIDataFile.AddRange(convertRawINCIToSSFormat(SSUtil.DeSerializeXMLObject<List<Closure>>(xmlINCIFolderPath + currentfilename)));
                //increment to read next file
                foreach (SSIncidentDataTable aINCIPoint in allDataInTheINCIDataFile)
                {
                    //identify any filtering needed - none to start with
                    finalPackageOfINCIDataAllRoutes.Add(aINCIPoint);
                }
                lastIntermediateTime = lastIntermediateTime.AddSeconds(fileSaveFreq);
                intermediateTime = intermediateTime.AddSeconds(fileSaveFreq);
            }
            allDataInTheINCIDataFile.Clear();//clear Temp list
            return finalPackageOfINCIDataAllRoutes;//return result list
        }
        private List<SSIncidentDataTable> convertRawINCIToSSFormat(List<Closure> downloadedINCIData)
        {
            List<SSIncidentDataTable> convertedData = new List<SSIncidentDataTable>();

            foreach (Closure aRawINCI in downloadedINCIData)
            {
                if (aRawINCI.Id != null)
                {
                    SSIncidentDataTable aConvertedData = new SSIncidentDataTable();

                    aConvertedData.SQID = -1;
                    aConvertedData.IncidentID = aRawINCI.Id;
                    aConvertedData.RoadName = aRawINCI.Road;
                    aConvertedData.NameOfLocation = aRawINCI.Name;
                    aConvertedData.DistrictType = (aRawINCI.District == "Etobicoke York" ? torontoDistrictType.EtobicokeYork : (aRawINCI.District == "North York" ? torontoDistrictType.NorthYork : (aRawINCI.District == "Scarborough" ? torontoDistrictType.Scarborough : (aRawINCI.District == "Toronto and East York" ? torontoDistrictType.TorontoandEastYork : torontoDistrictType.TorontoandEastYork))));//default: TorontoandEastYork
                    aConvertedData.Longitude = Convert.ToDouble(aRawINCI.Longitude);
                    aConvertedData.Latitude = Convert.ToDouble(aRawINCI.Latitude);
                    aConvertedData.RoadClass = (aRawINCI.RoadClass == "Expressway" ? roadClassType.Expressway : (aRawINCI.RoadClass == "Major Arterial" ? roadClassType.MajorArterial : (aRawINCI.RoadClass == "Minor Arterial" ? roadClassType.MinorArterial : (aRawINCI.RoadClass == "Collector" ? roadClassType.Collector : (aRawINCI.RoadClass == "Local" ? roadClassType.Local : roadClassType.MinorArterial)))));//default Major Arterial
                    aConvertedData.Planned = (aRawINCI.Planned == "1" ? true : false);
                    aConvertedData.SeverityOverride = (aRawINCI.SeverityOverride == "1" ? true : false);
                    aConvertedData.LastUpdatedTime = (Convert.ToInt64(aRawINCI.LastUpdated) / 1000);
                    aConvertedData.StartTime = (Convert.ToInt64(aRawINCI.StartTime) / 1000);
                    aConvertedData.EndTime = (aRawINCI.EndTime == "" ? (Convert.ToInt64(aRawINCI.LastUpdated) / 1000) : (Convert.ToInt64(aRawINCI.EndTime) / 1000));//if no endTime is specified, use last updated time as approximate end time.
                    aConvertedData.WorkPeriod = (aRawINCI.WorkPeriod.Equals("Emergency") ? workPeriodType.Emergency : (aRawINCI.WorkPeriod.Equals("Continuous") ? workPeriodType.Continuous : (aRawINCI.WorkPeriod.Equals("Daily") ? workPeriodType.Daily : (aRawINCI.WorkPeriod.Equals("Weekdays") ? workPeriodType.Weekdays : (aRawINCI.WorkPeriod.Equals("Weekends") ? workPeriodType.Weekends : workPeriodType.Weekends)))));
                    aConvertedData.Expired = (aRawINCI.Expired == "1" ? true : false);
                    aConvertedData.WorkEventCause = (aRawINCI.WorkPeriod.Equals("Emergency") ? workEventType.Emergency : (aRawINCI.WorkEventType == "Construction" ? workEventType.Construction : (aRawINCI.WorkEventType == "Maintenance" ? workEventType.Maintenance : (aRawINCI.WorkEventType == "Utility Cut" ? workEventType.UtilityCut : (aRawINCI.WorkEventType == "Special Event" ? workEventType.SpecialEvent : (aRawINCI.WorkEventType == "Filming" ? workEventType.Filming : (aRawINCI.WorkEventType == "Parade" ? workEventType.Parade : (aRawINCI.WorkEventType == "Demonstration" ? workEventType.Demonstration : (aRawINCI.WorkEventType == "Other" ? workEventType.Other : workEventType.Other)))))))));//default other
                    //note: workperiod emergency has been incorporated as a workeventcause!
                    aConvertedData.PermitType = (aRawINCI.PermitType == "RACS" ? closurePermitType.RACS : (aRawINCI.PermitType == "Street Event" ? closurePermitType.StreetEvent : (aRawINCI.PermitType == "Film" ? closurePermitType.Film : (aRawINCI.PermitType == "Other" ? closurePermitType.Other : closurePermitType.Other))));//default other
                    aConvertedData.ContractorNameInvolved = aRawINCI.Contractor;
                    aConvertedData.ClosureDescription = aRawINCI.Description;

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
    [XmlRoot(ElementName = "Closure")]
    public class Closure
    {
        [XmlElement(ElementName = "Id")]
        public string Id { get; set; }
        [XmlElement(ElementName = "Road")]
        public string Road { get; set; }
        [XmlElement(ElementName = "Name")]
        public string Name { get; set; }
        [XmlElement(ElementName = "District")]
        public string District { get; set; }
        [XmlElement(ElementName = "Latitude")]
        public string Latitude { get; set; }
        [XmlElement(ElementName = "Longitude")]
        public string Longitude { get; set; }
        [XmlElement(ElementName = "RoadClass")]
        public string RoadClass { get; set; }
        [XmlElement(ElementName = "Planned")]
        public string Planned { get; set; }
        [XmlElement(ElementName = "SeverityOverride")]
        public string SeverityOverride { get; set; }
        [XmlElement(ElementName = "Source")]
        public string Source { get; set; }
        [XmlElement(ElementName = "LastUpdated")]
        public string LastUpdated { get; set; }
        [XmlElement(ElementName = "StartTime")]
        public string StartTime { get; set; }
        [XmlElement(ElementName = "EndTime")]
        public string EndTime { get; set; }
        [XmlElement(ElementName = "WorkPeriod")]
        public string WorkPeriod { get; set; }
        [XmlElement(ElementName = "Expired")]
        public string Expired { get; set; }
        [XmlElement(ElementName = "Signing")]
        public string Signing { get; set; }
        [XmlElement(ElementName = "Notification")]
        public string Notification { get; set; }
        [XmlElement(ElementName = "WorkEventType")]
        public string WorkEventType { get; set; }
        [XmlElement(ElementName = "Contractor")]
        public string Contractor { get; set; }
        [XmlElement(ElementName = "PermitType")]
        public string PermitType { get; set; }
        [XmlElement(ElementName = "Description")]
        public string Description { get; set; }
        [XmlElement(ElementName = "SpecialEvent")]
        public string SpecialEvent { get; set; }
    }

    [XmlRoot(ElementName = "Closures")]
    public class Closures
    {
        [XmlElement(ElementName = "Closure")]
        public List<Closure> Closure { get; set; }
    }
    #endregion
}
