using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace MainClass.DataTools
{
    /** 
    *** Class object that can pull JSON webfile, and allow SSimulator to retrive prepared data.
    *** Designed to pull data within specified time range, for ALL routes available.
    **/
    public class SSWebCrawlerCVST
    {
        //MUST GET ACCESS AND LOG ON HERE BEFORE RUNNING THIS TOOL: http://portal.cvst.ca/
        private static Uri uri_CVSTbase = new Uri("http://portal.cvst.ca/api/0.1/ttc/");//give lastest update
        private string startupPath;
        private string jsonfolderpath;
        public Boolean online { get; set; }
        private string usernameCVST;
        private string passwordCVST;
        private bool cd_closed;
        private ChromeDriver cd = null; //must have installed chrome driver via nuget
        private long timeIncre = 3600; //1 hr

        //Data early process setting (get rid of out of Toronto data)
        private GeoLocation TorontoCentroid = new GeoLocation(43.7, -79.4);
        private double TorontoRadius = 25000;//far enough to edge of scarborough 

        /* Constructor for WebCrawlerCVST */
        public SSWebCrawlerCVST(string startupPathloc, string gpsJSONPathloc, long timeIncre, Boolean onl = false)
        {
            startupPath = startupPathloc;
            jsonfolderpath = gpsJSONPathloc;
            online = onl;
            usernameCVST = "";
            passwordCVST = "";
            cd_closed = true;
        }

        /// <summary>
        /// A: Construct Route List 
        /// </summary>
        /// <returns></returns>
        public List<string> RetrieveRouteData()
        {
            List<JSONRoute> currentRouteList;
            string filename = "routes";
            string filepath = jsonfolderpath + filename + @".json";
            Uri uri_RouteListdownload = new Uri(uri_CVSTbase, "routes");

            //if (online)//only download if online mode
            if (!(File.Exists(filepath) && !online)) // skip download if in offline mode, and file does exist
            {
                using (WebClient client = new WebClient())
                {
                    //download string and write to file
                    string json = client.DownloadString(uri_RouteListdownload);
                    string jsonString = Convert.ToString(json);
                    //Console.WriteLine(jsonString);//for diagnostic
                    using (StreamWriter outputFile = new StreamWriter(filepath, false)) //overwrite - does not append
                    {
                        outputFile.WriteLine(json);
                        outputFile.Close();
                    }
                }
            }
            // read directly from file, no string used
            using (StreamReader file = File.OpenText(filepath))
            {
                JsonSerializer serializer = new JsonSerializer();
                currentRouteList = (List<JSONRoute>)serializer.Deserialize(file, typeof(List<JSONRoute>));
            }
            // route data setting - remove ones not selected, if setting is not all.
            string route_setting = "all";//default is all
            string TempRoute;
            List<string> routeList = new List<string>();
            using (StreamReader file = File.OpenText(startupPath + "RouteRange.txt"))
            {
                route_setting = file.ReadLine();
                if (route_setting.Equals("select"))
                {
                    TempRoute = file.ReadLine();
                    while (TempRoute != null)
                    {
                        routeList.Add(TempRoute);
                        TempRoute = file.ReadLine();
                    }

                    List<JSONRoute> TempSelectRouteList = new List<JSONRoute>();
                    for (int i = 0; i < currentRouteList.Count; i++)
                    {
                        for (int j = 0; j < routeList.Count; j++)
                        {
                            if (currentRouteList[i].RouteTag.Equals(routeList[j]))
                            {
                                TempSelectRouteList.Add(currentRouteList[i]);
                                //i--;//fix i for removed item
                            }
                        }
                    }
                    currentRouteList = TempSelectRouteList;
                    return currentRouteList.Select(C => C.RouteTag).ToList();
                }//end if
                else
                {
                    return currentRouteList.Select(C => C.RouteTag).ToList();
                }
            }
        }

        /// <summary>
        /// B: Pull records within a given period - download any missing data from CVST
        ///  DATA INFO: Records for specific route in a given period: 
        /// http://portal.cvst.ca/api/0.1/ttc/name/<route_name>?starttime=<ts1>&endtime=<ts2>
        /// <ts> could be epoch time or string type with<YYYY><MM><DD>T<HH><MM><time-zone>
        /// </summary>
        /// <param name="RouteTags"></param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <returns></returns>
        public List<SSVehGPSDataTable> RetriveGPSData(List<string> RouteTags, DateTime startTime, DateTime endTime)
        {
            List<JSONVehLoc> finalPackageOfGPSDataForRoutes = new List<JSONVehLoc>();
            List<JSONVehLoc> packageOfGPSDataForRoutes = new List<JSONVehLoc>();
            List<JSONVehLoc> packetsOfGPSDataForRoutes = new List<JSONVehLoc>();
            string json = null;
            string filepath = jsonfolderpath + RouteTags[0] + "-" + SSUtil.DateTimeNamingFormat(startTime.ToLocalTime())
                + "-" + SSUtil.DateTimeNamingFormat(endTime.ToLocalTime()) + @".json";
            //URI format: ("http://portal.cvst.ca/api/0.1/ttc/name/<route_name>?starttime=<ts1>&endtime=<ts2>")
            //Uri uriToDownload = new Uri("http://portal.cvst.ca/api/0.1/ttc/name/100-Flemingdon%20Park?starttime=1451779200&endtime=1451779900");
            string baseGPSURI = "tag/" + RouteTags[0];
            string appendURI = baseGPSURI + @"?starttime=" + SSUtil.DateTimeToEpochTime(startTime.ToUniversalTime())
            + @"&endtime=" + SSUtil.DateTimeToEpochTime(endTime.ToUniversalTime());
            Uri uriToDownload = new Uri(uri_CVSTbase, appendURI);
            JsonSerializer serializer;

            //now log-in process is finished, start download specified routes in list
            for (int i = 0; i < RouteTags.Count; i++)
            {
                //Defining JSON file names for route data - initial
                filepath = jsonfolderpath + RouteTags[i] + "-" + SSUtil.DateTimeNamingFormat(startTime.ToLocalTime())
                + "-" + SSUtil.DateTimeNamingFormat(endTime.ToLocalTime()) + @".json";

                if (!(File.Exists(filepath) && !online)) // skip download if in offline mode, and file does exist
                {
                    //if download is needed, and username hasn't been inputted, asks for user info
                    if (usernameCVST == "" || usernameCVST == null || passwordCVST == "" || passwordCVST == null)
                    {
                        bool userFlag = true;
                        while (userFlag)
                            userFlag = !userAccessCVST();
                    }
                    //0. check if cd is already open, if not, open it as it will be needed
                    if (cd_closed)
                    {
                        //Run selenium only if online mode is on or file does not exist
                        //web driver will open once, until quitWebCrawler() is called
                        cd = new ChromeDriver(); //must have installed chrome driver via nuget
                        // log-in process
                        cd.Url = @"http://portal.cvst.ca/login";
                        cd.Navigate();
                        IWebElement e = cd.FindElementByName("username");
                        e.SendKeys(usernameCVST);
                        e = cd.FindElementByName("password");
                        e.SendKeys(passwordCVST);
                        //e.SendKeys(Keys.Enter);//workaround if below line doesn't work
                        e = cd.FindElementByXPath("/html/body/div/div/div/div/div[2]/form/fieldset/input");//can inspect and copy the element xpath in chrome
                        e.Click();
                        cd_closed = false;
                        //cd is now open, it'll remain open until uitWebCrawler() is called.
                    }
                    // 1. Download json file from web
                    webCrawlerRepeatRequest:
                    try
                    {
                        long timeDifference = SSUtil.DateTimeToEpochTime(endTime) - SSUtil.DateTimeToEpochTime(startTime);
                        int num_packets = (int)Math.Ceiling((double)timeDifference / (double)timeIncre);

                        DateTime intermediateStartTime = startTime;
                        DateTime intermediateEndTime = endTime.AddSeconds(-(num_packets - 1) * timeIncre);

                        for (int n = 0; n < num_packets; n++)
                        {
                            // 1. Download json file from web
                            baseGPSURI = "tag/" + RouteTags[i];
                            appendURI = baseGPSURI + @"?starttime=" + SSUtil.DateTimeToEpochTime(intermediateStartTime.ToUniversalTime())
                                + @"&endtime=" + SSUtil.DateTimeToEpochTime(intermediateEndTime.ToUniversalTime());
                            uriToDownload = new Uri(uri_CVSTbase, appendURI);

                            cd.Url = Convert.ToString(uriToDownload);
                            json = cd.PageSource;

                            json = json.Substring(121, json.Length - 121);//get rid of extra characters at the front
                            json = json.Substring(0, json.Length - 22);//get rid of extra characters at the end
                            packetsOfGPSDataForRoutes.AddRange(JsonConvert.DeserializeObject<List<JSONVehLoc>>(json));
                            intermediateStartTime = intermediateEndTime;
                            intermediateEndTime = intermediateEndTime.AddSeconds(timeIncre);
                        }

                        serializer = new JsonSerializer();
                        using (StreamWriter sw = new StreamWriter(filepath))
                        {
                            using (JsonWriter writer = new JsonTextWriter(sw))
                            {
                                writer.Formatting = Formatting.Indented;
                                serializer.Serialize(writer, packetsOfGPSDataForRoutes);
                                packetsOfGPSDataForRoutes = null;//clear list
                                packetsOfGPSDataForRoutes = new List<JSONVehLoc>();
                            }
                        }
                    }
                    catch (WebDriverException e1)//bad request, try again
                    {
                        goto webCrawlerRepeatRequest;
                    }
                    catch (JsonSerializationException e2)//bad string, try again
                    {
                        goto webCrawlerRepeatRequest;
                    }
                }
                // 3. Deserialize json data from local and append to master GPS data object
                // read directly from file, no string used
                //StreamReader file = File.OpenText(filepath);
                //serializer = new JsonSerializer();
                //packageOfGPSDataForRoutes.AddRange((List<JSONVehLoc>)serializer.Deserialize(file, typeof(List<JSONVehLoc>)));
                string jsonString = File.ReadAllText(filepath);
                packageOfGPSDataForRoutes.AddRange((List<JSONVehLoc>)JsonConvert.DeserializeObject(jsonString, typeof(List<JSONVehLoc>)));

                /* post-process for GPS data that are outside of city area - uses TorontoCentroid and a radius*/
                /* Toronto Centroid: 43.7, -79.4 */
                foreach (JSONVehLoc aPoint in packageOfGPSDataForRoutes)
                {
                    double distFromTorontoCentroid = SSUtil.GetGlobeDistance(TorontoCentroid.Latitude, TorontoCentroid.Longitude, aPoint.coordinates[1], aPoint.coordinates[0]);
                    if (distFromTorontoCentroid < TorontoRadius)
                    {
                        finalPackageOfGPSDataForRoutes.Add(aPoint);
                    }
                }
            }
            return convertRawGPSToSSFormat(finalPackageOfGPSDataForRoutes);
        }
        private List<SSVehGPSDataTable> convertRawGPSToSSFormat(List<JSONVehLoc> downloadedGPSData)
        {
            List<SSVehGPSDataTable> convertedData = new List<SSVehGPSDataTable>();

            foreach (JSONVehLoc aRawGPS in downloadedGPSData)
            {
                SSVehGPSDataTable aConvertedData = new SSVehGPSDataTable();

                string routeNum = aRawGPS.routeNumber;
                string dir = aRawGPS.dirTag == "NULL" ? "" : aRawGPS.dirTag.Split('_')[1];
                string RouteTag = aRawGPS.dirTag == "NULL" ? "" : aRawGPS.dirTag.Split('_')[2];
                string TripCode = string.Concat(routeNum, "_", RouteTag, "_", aRawGPS.VehID);

                aConvertedData.gpsID = -1;//unassigned
                aConvertedData.GPStime = aRawGPS.GPStime;
                aConvertedData.vehID = Convert.ToInt32(aRawGPS.VehID);
                aConvertedData.TripCode = TripCode;
                aConvertedData.Direction = Convert.ToInt32(dir);
                aConvertedData.Longitude = Convert.ToDouble(aRawGPS.coordinates[0]);
                aConvertedData.Latitude = Convert.ToDouble(aRawGPS.coordinates[1]);
                aConvertedData.GPStime = aRawGPS.GPStime;

                convertedData.Add(aConvertedData);
            }

            return convertedData;
        }
        private bool userAccessCVST()
        {
            // a prompt for CVST login Information

            ////read username and password from Console
            //Console.WriteLine("Please input your CVST username: ");
            //usernameCVST = Console.ReadLine();
            //Console.WriteLine("Please input your CVST password: ");
            //passwordCVST = Console.ReadLine();
            //Console.Clear();
            usernameCVST = "enter here";
            passwordCVST = "enter here";
            if (usernameCVST == "" || usernameCVST == null || passwordCVST == "" || passwordCVST == null)
                return false;//failed entry
            else
                return true;//entry is not blank - not nessccarily accepted by CVST
        }
        public void quitWebCrawler()
        {
            // Perform Cleanup Operations
            if (!cd_closed)
            {
                cd.Quit();
                cd_closed = true;
            }
        }
    }

    #region JSON Object Classes
    /**
    *** JSON Object Classes, made using: http://json2csharp.com/
    **/

    /* Raw vehicle location object from JSON format */
    public class JSONVehLoc
    {
        public int GPStime { get; set; }
        public List<double> coordinates { get; set; }
        public string dateTime { get; set; }
        public string dirTag { get; set; }
        public string Heading { get; set; }
        public string last_update { get; set; }
        public bool predictable { get; set; }
        public string routeNumber { get; set; }
        public string route_name { get; set; }
        public int timestamp { get; set; }
        public int VehID { get; set; }
    }

    public class JSONRoute
    {
        public object end_lat { get; set; }
        public object end_lon { get; set; }
        public string RouteTag { get; set; }//same as route number
        public object route_id { get; set; }
        public object start_lat { get; set; }
        public object start_lon { get; set; }
        public string title { get; set; }//route name used for index
    }
    #endregion
}
