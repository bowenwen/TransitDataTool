using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainClass.DataTools
{
    //API DEV NOTE:
    //===API CALL INSTRUCTION===
    //https://openweathermap.org/current
    //Cities in cycle
    //Description:
    //JSON returns data from cities laid within definite circle that is specified by center point ('lat', 'lon') and expected number of cities ('cnt') around this point.
    //Parameters:
    //lat Latitude
    //lon Longitude
    //callback functionName for JSONP callback.
    //cluster use server clustering of points. Possible values ??are [yes, no]
    //lang language [en , ru ... ]
    //cnt number of cities around the point that should be returned
    //Examples of API calls:
    //http://api.openweathermap.org/data/2.5/find?lat=55.5&lon=37.5&cnt=10

    public class SSWebCrawlerOpenWeather
    {
        string jsonWeatherFolderPath;
        string jsonForecastWeatherFolderPath;
        string jsonWeatherFilenameFormat; //save name datetime in UTC - avoid DST and EST confusion
        long fileSaveFreq; //time interval to save the permanent json file - DAILY = 3600 * 24
        long bkSaveFreq;
        //json raw file backup period - raw json files saved will be deleted after this time interval. = 2 * 60
        /* Calls 10min: 600 
         * Calls 1day: 50,000 
         * Threshold: 7,200 
         * Hourly forecast: 5 
         * Daily forecast: 0 
         * note: openweather requests that only one request per 10 mins from single device/api key
         * & upon no request, wait 10 mins before another
         */
        DateTime nextCountResetTime;
        long tenMinRequestCount;
        long tenMinRequestLimit;//for 50k daily limit
        private string OWM_APIKEY;

        List<SSWeatherDataTable> processedWeatherDataFromJSON;

        public SSWebCrawlerOpenWeather(string jsonFolderPath, long jsonFileBackupTimeInSecs, string APIKEY)//, long jsonFileTimeIncreInSecs
        {
            nextCountResetTime = new DateTime(DateTime.Now.Ticks);
            tenMinRequestCount = 0;
            tenMinRequestLimit = 3;//this limit equals to 43.2k daily call if each call has 100 cities
            jsonWeatherFolderPath = jsonFolderPath;
            jsonForecastWeatherFolderPath = jsonFolderPath + @"Forecasts\";
            //File Folder Check and Initiation
            if (!Directory.Exists(jsonWeatherFolderPath))
            {
                Directory.CreateDirectory(jsonWeatherFolderPath);
            }
            if (!Directory.Exists(jsonForecastWeatherFolderPath))
            {
                Directory.CreateDirectory(jsonForecastWeatherFolderPath);
            }
            fileSaveFreq = 3600 * 24;//how often files are saved
            bkSaveFreq = jsonFileBackupTimeInSecs;//how often backup files are saved
            processedWeatherDataFromJSON = new List<SSWeatherDataTable>();
            jsonWeatherFilenameFormat = "GTA-Weather-{0}-{1}.json";
            OWM_APIKEY = APIKEY;
        }

        //SAMPLE API CALL ADDRESS: http://api.openweathermap.org/data/2.5/find?lat=43.653318&lon=-79.384053&cnt=50&appid=xxxx
        //NOTE: query will take at least 10-15 seconds to get response. Polling period should be > 1 min to be safe.
        //INFO: https://openweathermap.org/weather-conditions
        //https://openweathermap.org/current

        /// <summary>
        /// Polling frequency should be equal or less than 20s for max polling rate
        /// </summary>
        /// <param name="pollingFrequency"></param>
        /// <param name="durationInSecs"></param>
        /// <returns></returns>
        private ConcurrentDictionary<Tuple<int, long>, List> downloadedWeatherJSONData_UNSAVED;
        private ConcurrentDictionary<Tuple<int, long>, List> downloadedForecastWeatherJSONData_UNSAVED;
        public int startLiveDownloadTask(DateTime pollingStart, DateTime pollingEnd, double pollingFrequency)
        {
            int downloadState = 0;
            //NOTE: polling freq: 60s < bk freq: 120s < save freq: 3600s * 24 = 1 day
            downloadedWeatherJSONData_UNSAVED = new ConcurrentDictionary<Tuple<int, long>, List>();//INCITime & VehID as tuple index
            downloadedForecastWeatherJSONData_UNSAVED = new ConcurrentDictionary<Tuple<int, long>, List>();//INCITime & VehID as tuple index

            List<List> lastDownloadedData = new List<List>();//check actual data downloaded - a package of last downloaded data
            long currentServerDelay;
            //lastDownloadedData.list[1].dt
            DateTime currentServerDownloadTime;
            DateTime nextDownloadTime;// = DateTime.Now.ToLocalTime().AddSeconds(pollingFrequency);
            DateTime nextForecastDownloadTime;// = DateTime.Now.ToLocalTime().AddSeconds(pollingFrequency);
            double forecast_pollingFrequency = 10 * 60;//poll every hour
            DateTime nextBackupTime;// = DateTime.Now.ToLocalTime().AddSeconds(bkSaveFreq);
            //DateTime nextSaveTime = DateTime.Now.ToLocalTime().AddSeconds(fileSaveFreq);
            DateTime nextSaveTime;// = pollingStart.Date.AddSeconds(pollingStart.Hour * 3600 - 1 + fileSaveFreq);//save on the last second of the next hour - time stamp save
            DateTime nextActualSaveTime;// = pollingStart.Date.AddSeconds(pollingStart.Hour * 3600 - 1 + fileSaveFreq);//appropriate save time based on server delay!
            long lastLogUpdatedSave = 0;
            string bkfilename = "current-WeatherJSON-bk.json";

            //load last saved backup, if it is from within the last 5 minutes
            if (File.Exists(jsonWeatherFolderPath + bkfilename) && File.Exists(jsonForecastWeatherFolderPath + bkfilename))
            {
                if ((DateTime.Now.DateTimeToEpochTime() - File.GetLastWriteTime(jsonWeatherFolderPath + bkfilename).DateTimeToEpochTime()) < (5 * 60))
                {
                    List<List> bkWeatherPoints = new List<List>();
                    bkWeatherPoints = DeSerializeJSONObject<List<List>>(jsonWeatherFolderPath + bkfilename);
                    foreach (List aWeatherPoint in bkWeatherPoints)
                    {
                        downloadedWeatherJSONData_UNSAVED.AddOrUpdate(new Tuple<int, long>(aWeatherPoint.id, aWeatherPoint.dt), aWeatherPoint, (k, v) => aWeatherPoint);//replaces existing if it exists
                    }
                    bkWeatherPoints = DeSerializeJSONObject<List<List>>(jsonForecastWeatherFolderPath + bkfilename);
                    foreach (List aWeatherPoint in bkWeatherPoints)
                    {
                        downloadedForecastWeatherJSONData_UNSAVED.AddOrUpdate(new Tuple<int, long>(aWeatherPoint.id, aWeatherPoint.dt), aWeatherPoint, (k, v) => aWeatherPoint);//replaces existing if it exists
                    }
                }
            }

            downloadCurrentWeatherJSONFromWeb();
            currentServerDownloadTime = downloadedWeatherJSONData_UNSAVED.Count == 0 ? DateTime.Now : SSUtil.EpochTimeToLocalDateTime(downloadedWeatherJSONData_UNSAVED.Values.Select(c => c.dt).ToList().Max());
            currentServerDelay = DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDownloadTime.DateTimeToEpochTime();//in secs
            //server delay will be accounted for, in the next polling request --> a delay results in later start and later stop download time
            if (pollingStart.DateTimeToEpochTime() >= (DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDelay))//future download - put thread to sleep until start - proper state
            {
                while (pollingStart.DateTimeToEpochTime() >= (DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDelay))
                {
                    //return pollingStart.DateTimeToEpochTime() - downloadStartTime.DateTimeToEpochTime();
                    Thread.Sleep(Convert.ToInt32(1000 * pollingFrequency));//check delay at polling frequency (minute), until it is time for download

                    downloadCurrentWeatherJSONFromWeb();//lastDownloadedData = 
                    currentServerDownloadTime = downloadedWeatherJSONData_UNSAVED.Count == 0 ? DateTime.Now : SSUtil.EpochTimeToLocalDateTime(downloadedWeatherJSONData_UNSAVED.Values.Select(c => c.dt).ToList().Max());
                    currentServerDelay = DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDownloadTime.DateTimeToEpochTime();//in secs
                }

                //start late as per server delays
                nextDownloadTime = pollingStart.AddSeconds(0 + currentServerDelay);
                nextForecastDownloadTime = pollingStart.AddSeconds(0);
                nextBackupTime = pollingStart.AddSeconds(0);//no delay for backup
                nextActualSaveTime = pollingStart.Date.AddSeconds(-1 + fileSaveFreq + currentServerDelay);//assume file save frequency is at the hour
                nextSaveTime = DateTime.Now.ToLocalTime().Date.AddSeconds(-1 + fileSaveFreq);//asume save frequencies are in the scale of days
            }
            else if ((pollingStart.DateTimeToEpochTime() <= (DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDelay)) && (pollingEnd.DateTimeToEpochTime() > (DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDelay)))//some missing downloads - start period is before download can take place
            {
                //start late as per server delays - save at the hour, polling appropriately
                downloadState = -1;
                nextDownloadTime = DateTime.Now.ToLocalTime().AddSeconds(0);//no delay for download
                nextForecastDownloadTime = DateTime.Now.ToLocalTime().AddSeconds(0);//no delay for download
                nextBackupTime = DateTime.Now.ToLocalTime().AddSeconds(0);//no delay for backup
                nextSaveTime = DateTime.Now.ToLocalTime().Date.AddSeconds(-1 + fileSaveFreq);//asume save frequencies are in the scale of days
                nextActualSaveTime = nextSaveTime.AddSeconds(currentServerDelay);//assume file save frequency is at the hour
            }
            else //if (pollingEnd.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())//nothing to download - end period is before download can take place
            {
                downloadState = -2;
                return downloadState;
            }

            //initial save message
            if (lastLogUpdatedSave != nextSaveTime.DateTimeToEpochTime())
            {
                lastLogUpdatedSave = nextSaveTime.DateTimeToEpochTime();
                LogUpdateEventArgs args = new LogUpdateEventArgs();
                args.logMessage = String.Format("Weather Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
                OnLogUpdate(args);
            }

            //download started after a proper wait - proper state, or start immediate if some data can be downloaded (state: -1)
            while (pollingEnd.DateTimeToEpochTime() >= (DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDelay))//delay completion of survey as per server delay
            {
                List<long> nextOpTime = new List<long>();
                nextOpTime.Add(nextDownloadTime.DateTimeToEpochTime());
                nextOpTime.Add(nextForecastDownloadTime.DateTimeToEpochTime());
                nextOpTime.Add(nextBackupTime.DateTimeToEpochTime());
                //nextOpTime.Add(nextSaveTime.DateTimeToEpochTime());
                nextOpTime.Add(nextActualSaveTime.DateTimeToEpochTime());
                nextOpTime.Sort();//ascending
                if ((nextOpTime[0] > DateTime.Now.ToLocalTime().DateTimeToEpochTime()))
                {
                    if (lastLogUpdatedSave != nextSaveTime.DateTimeToEpochTime())
                    {
                        lastLogUpdatedSave = nextSaveTime.DateTimeToEpochTime();
                        LogUpdateEventArgs args = new LogUpdateEventArgs();
                        args.logMessage = String.Format("Weather Upcoming Save: {0}.", nextSaveTime.DateTimeISO8601Format());
                        OnLogUpdate(args);
                    }

                    long sleepTime = nextOpTime[0] - DateTime.Now.ToLocalTime().DateTimeToEpochTime();
                    Thread.Sleep(Convert.ToInt32(1000 * sleepTime));//brief sleep in between operations
                }
                //this condition structure prioritize download to mem, then backup, then save
                //Download - current weather
                if (nextDownloadTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextDownloadTime = DateTime.Now.AddSeconds(pollingFrequency);//nextDownloadTime.AddSeconds(pollingFrequency);
                    downloadCurrentWeatherJSONFromWeb();//List<List> newWeatherPoints = 
                    currentServerDownloadTime = downloadedWeatherJSONData_UNSAVED.Count == 0 ? DateTime.Now : SSUtil.EpochTimeToLocalDateTime(downloadedWeatherJSONData_UNSAVED.Values.Select(c => c.dt).ToList().Max());
                    if ((DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDownloadTime.DateTimeToEpochTime()) > 0)//data cannot be from the future
                    {
                        currentServerDelay = DateTime.Now.ToLocalTime().DateTimeToEpochTime() - currentServerDownloadTime.DateTimeToEpochTime();//in secs
                    }
                    else
                    {
                        currentServerDelay = 0;
                    }
                    //update next save time based on server delay
                    nextActualSaveTime = nextSaveTime.AddSeconds(currentServerDelay);//approximated next actual save time
                }
                //Download - Forecast weather
                if (nextForecastDownloadTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextForecastDownloadTime = DateTime.Now.AddSeconds(forecast_pollingFrequency);//nextDownloadTime.AddSeconds(pollingFrequency);
                    downloadCurrentWeatherJSONFromWeb(true);//List<List> newWeatherPoints = 
                }
                //Backup
                if (nextBackupTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    nextBackupTime = DateTime.Now.Subtract(nextBackupTime).TotalSeconds > bkSaveFreq ? DateTime.Now.AddSeconds(bkSaveFreq) : nextBackupTime.AddSeconds(bkSaveFreq);
                    SerializeJSONObject(downloadedWeatherJSONData_UNSAVED.Values.ToList(), jsonWeatherFolderPath + bkfilename);
                    SerializeJSONObject(downloadedForecastWeatherJSONData_UNSAVED.Values.ToList(), jsonForecastWeatherFolderPath + bkfilename);
                }
                //Save
                if (nextActualSaveTime.DateTimeToEpochTime() <= DateTime.Now.ToLocalTime().DateTimeToEpochTime())
                {
                    if (downloadedWeatherJSONData_UNSAVED.Values.Count > 0)//avoid saving empty file
                    {
                        string newfilename = string.Format(jsonWeatherFilenameFormat, nextSaveTime.AddSeconds(-fileSaveFreq + 1).ToUniversalTime().DateTimeNamingFormat(), nextSaveTime.ToUniversalTime().DateTimeNamingFormat());
                        SerializeJSONObject(downloadedWeatherJSONData_UNSAVED.Values.ToList(), jsonWeatherFolderPath + newfilename);
                        SerializeJSONObject(downloadedForecastWeatherJSONData_UNSAVED.Values.ToList(), jsonForecastWeatherFolderPath + newfilename);
                        downloadedWeatherJSONData_UNSAVED.Clear();//clear mem
                        downloadedForecastWeatherJSONData_UNSAVED.Clear();//clear mem
                    }
                    nextSaveTime = nextSaveTime.AddSeconds(fileSaveFreq);//next save time stamp
                    nextActualSaveTime = nextSaveTime.AddSeconds(currentServerDelay);//approximated next actual save time
                }
            }
            //clear backupFiles
            return downloadState;
            //downloadState = -2;//no download has taken place, date out of range
            //downloadState = -1;//download completed with some error - some missing data
            //downloadState = 0;//download completed with no error
        }
        //List<List> 
        private async void downloadCurrentWeatherJSONFromWeb(bool isForecastRequest = false)//download current Weather JSON data from NextBus with processing for INCITime
        {
            repeatWebDownload:
            try
            {
                if (isForecastRequest)
                {
                    List<RootObject> downloadedWeatherJSON = new List<RootObject>();
                    List<Uri> uriDownloadsForForecast = new List<Uri>();//forecasts are for 3 hrs, only need to be called at most once per hours
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=6167863&cnt=1&appid={0}", OWM_APIKEY)));//Downtown Toronto
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=7871305&cnt=1&appid={0}", OWM_APIKEY)));//Downsview
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=6121648&cnt=1&appid={0}", OWM_APIKEY)));//Rexdale
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=5950267&cnt=1&appid={0}", OWM_APIKEY)));//Etobicoke
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=6165683&cnt=1&appid={0}", OWM_APIKEY)));//Thornhill
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=6173577&cnt=1&appid={0}", OWM_APIKEY)));//Vaughan
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=6141899&cnt=1&appid={0}", OWM_APIKEY)));//Scarborough Station
                    uriDownloadsForForecast.Add(new Uri(string.Format("http://api.openweathermap.org/data/2.5/forecast?id=5882599&cnt=1&appid={0}", OWM_APIKEY)));//Agincourt
                    foreach (Uri uriToDownload in uriDownloadsForForecast)
                    {
                        var client = new WebClient();
                        string jsonString = await client.DownloadStringTaskAsync(uriToDownload);
                        //action(data);Action<string> action, 
                        downloadedWeatherJSON.Add((JsonConvert.DeserializeObject<RootObject>(jsonString)));
                        foreach (RootObject weatherData in downloadedWeatherJSON)
                        {
                            foreach (List aWeatherPoint in weatherData.list)
                            {
                                aWeatherPoint.id = weatherData.city.id;
                                aWeatherPoint.name = weatherData.city.name;
                                if (aWeatherPoint.rain == null)
                                {
                                    aWeatherPoint.rain = new Rain();
                                    aWeatherPoint.rain.rain3h = 0.0;
                                }
                                if (aWeatherPoint.snow == null)
                                {
                                    aWeatherPoint.snow = new Snow();
                                    aWeatherPoint.snow.snow3h = 0.0;
                                }
                                aWeatherPoint.coord = weatherData.city.coord;
                                downloadedForecastWeatherJSONData_UNSAVED.AddOrUpdate(new Tuple<int, long>(aWeatherPoint.id, aWeatherPoint.dt), aWeatherPoint, (k, v) => aWeatherPoint);//replaces existing if it exists
                            }
                        }
                    }
                }
                else
                {
                    List<RootObject> downloadedWeatherJSON = new List<RootObject>();
                    Uri uriToDownload = new Uri(string.Format("http://api.openweathermap.org/data/2.5/find?lat=43.653318&lon=-79.384053&cnt=35&appid={0}", OWM_APIKEY));
                    var client = new WebClient();
                    string jsonString = await client.DownloadStringTaskAsync(uriToDownload);
                    //action(data);Action<string> action, 
                    downloadedWeatherJSON.Add((JsonConvert.DeserializeObject<RootObject>(jsonString)));
                    foreach (RootObject weatherData in downloadedWeatherJSON)
                    {
                        foreach (List aWeatherPoint in weatherData.list)
                        {
                            downloadedWeatherJSONData_UNSAVED.AddOrUpdate(new Tuple<int, long>(aWeatherPoint.id, aWeatherPoint.dt), aWeatherPoint, (k, v) => aWeatherPoint);//replaces existing if it exists
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (isForecastRequest)
                {
                    //wait a minute before next request
                    Thread.Sleep(Convert.ToInt32(1000 * (1 * 60)));
                    //then retry until ten minute request limit is reached 
                    goto repeatWebDownload;
                }
                //if it is not forecast download, ignore since the next download would happen very soon
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="RouteTags"></param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <returns></returns>
        public List<SSWeatherDataTable> RetriveWeatherData(DateTime startTime, DateTime endTime, bool forecastWeather = false)
        {
            List<SSWeatherDataTable> finalPackageOfWeatherDataAllRoutes = new List<SSWeatherDataTable>();
            List<SSWeatherDataTable> allDataInTheWeatherDataFile = new List<SSWeatherDataTable>();

            //For incident and weather data, saves are daily
            startTime = startTime.Date;//beginning of first day
            endTime = endTime.Date.AddDays(1).AddSeconds(-1);//end of second day

            DateTime lastIntermediateTime = startTime;//intermediate time 59:59 (min:sec) from start
            DateTime intermediateTime = startTime.Date.AddHours(startTime.Hour).AddSeconds(fileSaveFreq - 1);//intermediate time 59:59 (min:sec) from start

            while (intermediateTime.DateTimeToEpochTime() <= endTime.DateTimeToEpochTime())
            {
                string currentfilename = string.Format(jsonWeatherFilenameFormat, lastIntermediateTime.ToUniversalTime().DateTimeNamingFormat(), intermediateTime.ToUniversalTime().DateTimeNamingFormat());
                if (forecastWeather)
                {
                    allDataInTheWeatherDataFile.AddRange(convertDataToSSFormat(DeSerializeJSONObject<List<List>>(jsonForecastWeatherFolderPath + currentfilename)));
                }
                else
                {
                    //load current weather data by default
                    if (File.Exists(jsonWeatherFolderPath + currentfilename))
                    {
                        allDataInTheWeatherDataFile.AddRange(convertDataToSSFormat(DeSerializeJSONObject<List<List>>(jsonWeatherFolderPath + currentfilename)));
                    }
                    //if current weather doesn't exist, automatically try to load forecast data
                    else
                    {
                        allDataInTheWeatherDataFile.AddRange(convertDataToSSFormat(DeSerializeJSONObject<List<List>>(jsonForecastWeatherFolderPath + currentfilename)));
                    }
                }
                //increment to read next file
                foreach (SSWeatherDataTable aWeatherPoint in allDataInTheWeatherDataFile)
                {
                    //identify any filtering needed - none to start with
                    finalPackageOfWeatherDataAllRoutes.Add(aWeatherPoint);
                }
                lastIntermediateTime = lastIntermediateTime.AddSeconds(fileSaveFreq);
                intermediateTime = intermediateTime.AddSeconds(fileSaveFreq);
            }
            allDataInTheWeatherDataFile.Clear();//clear Temp list
            return finalPackageOfWeatherDataAllRoutes;//return result list
        }
        private List<SSWeatherDataTable> convertDataToSSFormat(List<List> downloadedWeatherData)
        {
            List<SSWeatherDataTable> convertedData = new List<SSWeatherDataTable>();

            foreach (List aRawWeather in downloadedWeatherData)
            {
                if (aRawWeather.weather != null)
                {
                    SSWeatherDataTable aConvertedData = new SSWeatherDataTable();

                    aConvertedData.SQID = -1;
                    aConvertedData.WeatherStationID = aRawWeather.id;
                    aConvertedData.WeatherTime = aRawWeather.dt;
                    aConvertedData.Longitude = aRawWeather.coord.lon;
                    aConvertedData.Latitude = aRawWeather.coord.lat;
                    int weatherCondID = aRawWeather.weather.First().id;
                    aConvertedData.WeatherCondition = (weatherCondID >= 951 ? weatherType.Additional : (weatherCondID >= 900 ? weatherType.Extreme : (weatherCondID >= 801 ? weatherType.Clouds : (weatherCondID >= 800 ? weatherType.Clear : (weatherCondID >= 701 ? weatherType.Atmosphere : (weatherCondID >= 600 ? weatherType.Snow : (weatherCondID >= 500 ? weatherType.Rain : (weatherCondID >= 300 ? weatherType.Drizzle : (weatherCondID >= 200 ? weatherType.Thunderstorm : weatherType.Additional)))))))));//default: additional
                    aConvertedData.Temp = Math.Round((aRawWeather.main.temp - 273.15), 2); //change K to oC
                    aConvertedData.Humidity = aRawWeather.main.humidity;
                    aConvertedData.WindSpeed = aRawWeather.wind.speed;
                    aConvertedData.WeatherStationName = aRawWeather.name;
                    aConvertedData.RainPptnThreeHr = aRawWeather.rain == null ? 0 : aRawWeather.rain.rain3h;
                    aConvertedData.SnowPptnThreeHr = aRawWeather.snow == null ? 0 : aRawWeather.snow.snow3h;

                    convertedData.Add(aConvertedData);
                }
            }
            return convertedData;
        }

        /// <summary>
        /// Serializes a JSON file into an object list
        /// Ref: http://stackoverflow.com/questions/6115721/how-to-save-restore-serializable-object-to-from-file
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public void SerializeJSONObject<T>(T serializableObject, string fileName)
        {
            if (serializableObject == null) { return; }

            try
            {
                string output = JsonConvert.SerializeObject(serializableObject);
                File.WriteAllText(fileName, output);
                //JsonSerializer serializer = new JsonSerializer();
                //using (StreamWriter sw = new StreamWriter(fileName))
                //{
                //    using (JsonWriter writer = new JsonTextWriter(sw))
                //    {
                //        writer.Formatting = Formatting.Indented;
                //        serializer.Serialize(writer, serializableObject);
                //        writer.Close();
                //    }
                //}
            }
            catch (Exception ex)
            {
                //Log exception here
                LogUpdateEventArgs args = new LogUpdateEventArgs();
                args.logMessage = String.Format("Exception serializing file: {0}.", fileName);
                OnLogUpdate(args);
            }
        }

        /// <summary>
        /// Deserializes an json file into an object list
        /// Ref: http://stackoverflow.com/questions/6115721/how-to-save-restore-serializable-object-to-from-file
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public T DeSerializeJSONObject<T>(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) { return default(T); }

            T objectOut = default(T);

            try
            {
                string jsonString = File.ReadAllText(fileName);
                objectOut = JsonConvert.DeserializeObject<T>(jsonString);//((T)JsonConvert.DeserializeObject(jsonString, typeof(T)));
            }
            catch (Exception ex)
            {
                //Log exception here
                LogUpdateEventArgs args = new LogUpdateEventArgs();
                args.logMessage = String.Format("Exception deserializing file: {0}.", fileName);
                OnLogUpdate(args);
            }
            return objectOut;
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

    #region json object classes
    [DataContract]
    public class Coord
    {
        [DataMember]
        public double lon { get; set; }
        [DataMember]
        public double lat { get; set; }
    }
    [DataContract]
    public class Sys
    {
        [DataMember]
        public string country { get; set; }
        [DataMember]
        public int population { get; set; }
        //add from Sys2
        [DataMember]
        public string pod { get; set; }
    }
    //[DataContract]
    //public class Sys2
    //{
    //    [DataMember]
    //    public string pod { get; set; }
    //}
    [DataContract]
    public class City
    {
        [DataMember]
        public int id { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public Coord coord { get; set; }
        [DataMember]
        public string country { get; set; }
        [DataMember]
        public int population { get; set; }
        [DataMember]
        public Sys sys { get; set; }
    }
    [DataContract]
    public class Main
    {
        [DataMember]
        public double temp { get; set; }
        [DataMember]
        public double temp_min { get; set; }
        [DataMember]
        public double temp_max { get; set; }
        [DataMember]
        public double pressure { get; set; }
        [DataMember]
        public double sea_level { get; set; }
        [DataMember]
        public double grnd_level { get; set; }
        [DataMember]
        public int humidity { get; set; }
        [DataMember]
        public double temp_kf { get; set; }
    }
    [DataContract]
    public class Weather
    {
        [DataMember]
        public int id { get; set; }
        [DataMember]
        public string main { get; set; }
        [DataMember]
        public string description { get; set; }
        [DataMember]
        public string icon { get; set; }
    }
    [DataContract]
    public class Clouds
    {
        [DataMember]
        public int all { get; set; }
    }
    [DataContract]
    public class Wind
    {
        [DataMember]
        public double speed { get; set; }
        [DataMember]
        public double deg { get; set; }
    }
    [DataContract]
    public class Snow
    {
        [DataMember]
        [JsonProperty(PropertyName = "3h")]
        public double snow3h { get; set; }
    }
    [DataContract]
    public class Rain
    {
        [DataMember]
        [JsonProperty(PropertyName = "3h")]
        public double rain3h { get; set; }
    }
    [DataContract]
    public class List
    {
        [DataMember]
        public int id { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public Coord coord { get; set; }
        [DataMember]
        public Main main { get; set; }
        [DataMember]
        public int dt { get; set; }
        [DataMember]
        public Wind wind { get; set; }
        [DataMember]
        public Sys sys { get; set; }
        [DataMember]
        public Clouds clouds { get; set; }
        [DataMember]
        public List<Weather> weather { get; set; }
        //[DataMember]
        //public Sys2 sys { get; set; }
        [DataMember]
        public Snow snow { get; set; }
        [DataMember]
        public Rain rain { get; set; }
        [DataMember]
        public string dt_txt { get; set; }
    }
    [DataContract]
    public class RootObject
    {
        [DataMember]
        public City city { get; set; }
        [DataMember]
        public string message { get; set; }
        [DataMember]
        public string cod { get; set; }
        [DataMember]
        public int count { get; set; }
        [DataMember]
        public int cnt { get; set; }
        [DataMember]
        public List<List> list { get; set; }
    }
    #endregion
}
