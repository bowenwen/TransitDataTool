using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MainClass{
    #region SSim OBJECT CLASSES - Variable & Data Classes

    /* 
     * Pre-Analysis: Trip-Based Travel Time Analysis
     * Processed GPS data for Modelling.
     * A trip-based analysis can be used to generate agent-based trips
     * 
     */

    [Serializable]
    public class SSLinkObjectTable
    {
        public int LINKID { get; set; }
        public int GtfsScheduleID { get; set; }
        public int StartStopID { get; set; }
        public int EndStopID { get; set; }
        //public string StartStopLocTyp { get; set; }
        //public string EndStopLocTyp { get; set; }
        //NOTE: stop location types are not stored in object or database, instead query them off of stoploctype object or db tables
        public double LinkDist { get; set; }//needs processing
        public List<int> IntxnIDsAll { get; set; }
        /// <summary>
        /// (Size: 2)
        /// isStreetcar,isSeparatedROW
        /// </summary>
        public List<double> OtherRouteVars { get; set; }
        /// <summary>
        /// (Size: 10)
        /// Num_VehLtTurns,Num_VehRtTurns,num_intxn,Num_TSP_equipped,Num_PedCross,Sum_SigIntxnApproach,AvgVehVol,AvgPedVol,isStartStopNearSided,isEndStopFarSided
        /// </summary>
        public List<double> OtherLinkVars { get; set; }
        public List<SSLinkDataTable> LinkData { get; set; }

        public SSLinkObjectTable()
        {
            IntxnIDsAll = new List<int>();
            LinkData = new List<SSLinkDataTable>();
        }
    }

    [Serializable]
    public class SSLinkDataTable
    {
        public int LINKDATAID { get; set; }
        /// <summary>
        /// 0-neither, 1-train, 2-test
        /// </summary>
        public int TrainOrTestData { get; set; } //0-neither, 1-train, 2-test
        public int LINKID { get; set; }
        /// <summary>
        /// Arrival time at start of the link
        /// </summary>
        public long GPSTimeAtLinkStart { get; set; }//needs processing
        public double RunningTime { get; set; }//techinically don't need since SSVehTripDataTable stores it
        public double StartStopDwellTime { get; set; }//techinically don't need since SSVehTripDataTable stores it
        public double EndStopDwellTime { get; set; }//techinically don't need since SSVehTripDataTable stores it
        public double DelayAtStart { get; set; }//techinically don't need since SSVehTripDataTable stores it
        public double HeadwayAtStart { get; set; }//techinically don't need since SSVehTripDataTable stores it
        public int WeatherSQID { get; set; }
        public int IncidentSQID { get; set; }
        /// <summary>
        /// the identification for gps/avl trip id
        /// </summary>
        public int TripID { get; set; }

        /// <summary>
        /// compute running time from given link distance
        /// </summary>
        /// <param name="inputLinkDist"></param>
        /// <returns></returns>
        public double getRunningSpeed(double inputLinkDist)
        {
            double avgRunningSpeed = Math.Round((inputLinkDist / (this.RunningTime) * 3.6), 1);// m/s ==> km/hr// 
            return (avgRunningSpeed <= 0) || (avgRunningSpeed > 120) ? -1 : avgRunningSpeed;//return only positives, otherwise it is 0.0 (has not moved)
        }


        /// <summary>
        /// compute travel time from given link distance. travel time is from start stop departure to end stop departure
        /// </summary>
        /// <param name="inputLinkDist"></param>
        /// <returns></returns>
        public double getTravelSpeed(double inputLinkDist)
        {
            //arrival to arrival
            double avgTravelgSpeed = Math.Round((inputLinkDist / (this.RunningTime + this.StartStopDwellTime) * 3.6), 1);// m/s ==> km/hr// 
                                                                                                                         //departure to departure
                                                                                                                         //double avgTravelgSpeed = Math.Round((inputLinkDist / (this.RunningTime + this.EndStopDwellTime) * 3.6), 1);// m/s ==> km/hr// 
            return (avgTravelgSpeed <= 0) || (avgTravelgSpeed > 120) ? -1 : avgTravelgSpeed;//return only positives, otherwise it is 0.0 (has not moved)
        }
    }

    [Serializable]
    public class SSVehTripDataTable
    {
        //public int agencyID { get; set; }//1 for TTC -> link to route_groups table, not needed now.
        //public int serviceID { get; set; }//compute from day of the week - ignore for now, need to read GTFS calendar to verify date range

        public int TripID { get; set; }//unique id for SSim trips
        public string TripCode { get; set; }//concatenated code for SSim trips: veh#_RouteCode_RouteTag_dir

        //directly from raw VehLoc Obj - for reference
        public string RouteCode { get; set; }//route number for route group, get from routeNumber -> key to route_groups table
        public int Direction { get; set; }//Direction of the bus, 0 (SB, EB) or  or 1 (NB, WB)
        public string RouteTag { get; set; }//route number with letter or sub-route info, i.e.: 102D

        //public List<SSimVehGPSData> vehLoc { get; set; }// all coordinates the vehicle has been
        public List<int> GPSIDs { get; set; } //record # that correspond with TTCGPS
        public long startGPSTime { get; set; } //record time of the first GPS point in the trip
                                               //public int gtfsTripID { get; set; } //schedule ID finds a short list of trips, matching of departure time yields trip ID
        public int GtfsScheduleID { get; set; } //service ID & Route ID finds the corresponding schedule

        public List<int> tripStopIDs { get; set; }
        public List<double> tripStopDistances { get; set; }
        public List<TimeSpan> tripStopArrTimes { get; set; }
        /// <summary>
        /// The Estimated Dwell Times at stop
        /// measured in seconds
        /// </summary>
        public List<double> tripDwellTimes { get; set; }//measured in seconds        
                                                        /// <summary>
                                                        /// The Estimated Delay at stop 
                                                        /// (differece between schedule and observed stop times)
                                                        /// measured in seconds
                                                        /// </summary>
        public List<double> tripEstiDelays { get; set; }
        /// <summary>
        /// The Estimated Observed Headways at stop 
        /// </summary>
        public List<double> tripEstiHeadways { get; set; }
        /// <summary>
        /// The Schedule Headways at stop 
        /// (elements should be same, but kept as a list just in case, as it may be changed)
        /// </summary>
        public List<double> tripScheduleHeadways { get; set; }
        /// <summary>
        /// Previous Trip ID to this Trip at stop
        /// (elements should be same, but kept as a list just in case, as it may be changed, if it is first trip, prev. trip id would be -1)
        /// </summary>
        public List<int> tripPrevTripIDs { get; set; }
        /// <summary>
        /// Organized GPS ID relative to stop locations and ids 
        /// (software object - index stop id of next stop)
        /// </summary>
        public Dictionary<int, List<Tuple<int, double, TimeSpan>>> gpsIDShapedistTime_ByStopID { get; set; }

        //public Dictionary<int, double> gpsPointShapeDistances { get; set; } - not needed, can query off of vehGPSPointsTable
        //format for TripCode: RouteCode_VehicleID_DirID
    }

    [Serializable]
    public class SSVehGPSDataTable
    {
        public int gpsID { get; set; }//GPSID to read information from TTCGPS
        public long GPStime { get; set; }//epoch time
        public int vehID { get; set; }//Vehicle ID - record purpose
        public double Longitude { get; set; }//Longitude -> equivalent to 
        public double Latitude { get; set; }//Latitude
                                            //nearest stops on route
                                            //public string PrevStop { get; set; }
                                            //public string NextStop { get; set; }
        public int Direction { get; set; }
        public int Heading { get; set; }
        public string TripCode { get; set; }
        public int PrevStop { get; set; }
        public int NextStop { get; set; }
        public double DistFromShapeStart { get; set; }//dist to next stop
        public double DistToNextStop { get; set; }
        //public double bearingToStopA { get; set; }
        //public double bearingToStopB { get; set; }
        //public double EstiHeadway { get; set; }//headway in terms of minutes
        //public double EstiDwellTime { get; set; }//estimated dwell time
        //public double EstiStopTime { get; set; }//estimated stop time, in traffic or in park
        //public double EstiScheduleDelay { get; set; }//estimated difference between scheduled desired arrival vs actual
        //public weatherType nearbyWeatherCondition { get; set; }//weather type influencing this GPS point
        //public workEventType roadRestrictionEvent { get; set; }//road restriction event influencing this GPS point
        //public int closestWeatherID { get; set; }
        //public int downstreamIncidentID { get; set; }//only if close enough, at downstream of the route
        public double EstiAvgSpeed { get; set; }//surveyed travel times - in km/hr - up to this stop
        public int serviceID { get; set; }
        public int TripID { get; set; }
        public int routeID { get; set; }
    }

    [Serializable]
    public class SSIncidentDataTable
    {
        public int SQID { get; set; }
        public string IncidentID { get; set; }
        public string RoadName { get; set; }
        public string NameOfLocation { get; set; }
        public torontoDistrictType DistrictType { get; set; }
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public roadClassType RoadClass { get; set; }
        public bool Planned { get; set; }//planned or an emergency
        public bool SeverityOverride { get; set; }//Indicates that the closure impact has special importance (i.e.full F.G.Gardiner closure)
                                                  //public string Source { get; set; }
        public long LastUpdatedTime { get; set; }//epoch in sec
        public long StartTime { get; set; }//epoch in sec
        public long EndTime { get; set; }//epoch in sec
        public workPeriodType WorkPeriod { get; set; }
        public bool Expired { get; set; }//1 yes 0 no
                                         //public string Signing { get; set; }
                                         //public string Notification { get; set; }
        public workEventType WorkEventCause { get; set; }
        public closurePermitType PermitType { get; set; }
        public string ContractorNameInvolved { get; set; }
        public string ClosureDescription { get; set; }
        //public string SpecialEvent { get; set; }//type is sufficient for now
    }
    public enum roadClassType
    {
        Expressway,
        MajorArterial,
        MinorArterial,
        Collector,
        Local
    }
    public enum torontoDistrictType
    {
        EtobicokeYork,
        NorthYork,
        Scarborough,
        TorontoandEastYork
    }
    public enum workPeriodType
    {
        Emergency,
        Continuous,
        Daily,
        Weekdays,
        Weekends
    }
    public enum workEventType
    {
        NoRoadRestriction = 0,//for modelling purpose - default unless assigned otherwise
        Emergency = 1,//mixed with workPeriodType
        Construction,
        Maintenance,
        UtilityCut,
        SpecialEvent,
        Filming,
        Parade,
        Demonstration,
        Other
    }
    public enum closurePermitType
    {
        RACS,
        StreetEvent,
        Film,
        Other
    }

    [Serializable]
    public class SSWeatherDataTable
    {
        //WeatherStationID and WeatherTime determines uniqueness
        public int SQID { get; set; }
        public int WeatherStationID { get; set; }
        public long WeatherTime { get; set; }//epoch time
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public weatherType WeatherCondition { get; set; }
        /// <summary>
        /// in degree Celsius (oC)
        /// </summary>
        public double Temp { get; set; } //oC - converted from K
        public double Humidity { get; set; } //%
        public double WindSpeed { get; set; } //%
        public string WeatherStationName { get; set; }
        public double RainPptnThreeHr { get; set; }//mm
        public double SnowPptnThreeHr { get; set; }//mm
    }
    public enum weatherType
    {
        //https://openweathermap.org/weather-conditions
        //Must check text containing these strings in this ORDER!!!
        Thunderstorm,
        Drizzle,
        Rain,
        Snow,//separate words:
             //snow
             //sleet
        Atmosphere,//multuple words:
                   //mist
                   //smoke
                   //haze
                   //sand, dust whirls
                   //fog
                   //sand
                   //dust
                   //volcanic ash
                   //squalls
                   //tornado (exclude from Atomsphere to include for extreme)
        Clear,
        Clouds,
        Extreme,//multuple words:
                //tornado
                //tropical storm
                //hurricane
                //cold
                //hot
                //windy
                //hail
        Additional//unknown or rare events
    }

    [Serializable]
    public class SSINTNXSignalDataTable
    {
        //WeatherStationID and WeatherTime determines uniqueness
        public int pxID { get; set; }
        public string mainStreet { get; set; }
        public string midblockStreet { get; set; }
        public string sideStreet1 { get; set; }
        public string sideStreet2 { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        //fields from "traffic_vols"
        public DateTime countDate { get; set; }//date of activation
        public int vehVol { get; set; }//8HrVehVol
        public int pedVol { get; set; }//8HrPedVol
                                       //fields from "traffic_signals"
        public string signal_system { get; set; }
        public string mode_of_control { get; set; }
        public bool isNotFXT { get; set; }//if signal is NOT on fixed timing
        public bool transit_preempt { get; set; }
        public bool fire_preempt { get; set; }
        public bool Rail_preempt { get; set; }
        public int no_of_signalized_approaches { get; set; }
        public bool isSignalizedIntxn { get; set; }
        //fields from "pedestrian_crossovers"
        public bool isPedCross { get; set; }
        //fields from "flashing_beacons"
        public bool isFlasingBeacons { get; set; }
    }

    //enum integer data for time period based on TTC Service Changes
    public enum seasonalScheduleType
    {
        January_early,//after this but before February_mid
        February_mid,
        March_late,
        May_early,
        June_mid,
        July_late,
        September_early,
        October_early,
        November_mid,
        December_mid,
        /*
         * FROM TTC SERVICE SUMMARY:
         * Service Change Dates
         * January 3, 2016
         * February 14, 2016
         * March 27, 2016
         * May 8, 2016
         * June 19, 2016
         * July 31, 2016
         * September 4, 2016
         * October 9, 2016
         * November 20, 2016
         * December 18, 2016
         * January 1, 2017
         * 
         */
    }

    // uses hour of the day to determine the seasonal schedule date period
    public class timePeriodDefinition
    {
        public double MorningPeak { get; set; }
        public double Midday { get; set; }
        public double AfternoonPeak { get; set; }
        public double EarlyEvening { get; set; }
        public double LateEvening { get; set; }
        public double Overnight { get; set; }
        public double Weekend_EarlyMorning { get; set; }
        public double Weekend_Morning { get; set; }
        public double Weekend_Afternoon { get; set; }
        public double Weekend_EarlyEvening { get; set; }
        public double Weekend_LateEvening { get; set; }
        public double Weekend_Overnight { get; set; }

        public timePeriodDefinition()//use predefined
        {
            //hours which the period starts, ends at the next one
            MorningPeak = 6;//600a to 900a
            Midday = 9;//900a to 300p 
            AfternoonPeak = 3 + 12;//300p to 700p
            EarlyEvening = 7 + 12;//700p to 1000p 
            LateEvening = 10 + 12;//1000p to 100a
            Overnight = 1;//130a to 530a  - 1 and 6
                          //Weekend
            Weekend_EarlyMorning = 6;
            Weekend_Morning = 8;
            Weekend_Afternoon = 12;
            Weekend_EarlyEvening = 7;
            Weekend_LateEvening = 10;
            Weekend_Overnight = 1;
        }
        public timePeriodDefinition(List<DateTime> startTimes)//given new def
        {
            if (startTimes.Count == 12)
            {
                MorningPeak = startTimes[0].Hour + startTimes[0].Minute / 60;
                Midday = startTimes[1].Hour + startTimes[1].Minute / 60;
                AfternoonPeak = startTimes[2].Hour + startTimes[2].Minute / 60;
                EarlyEvening = startTimes[3].Hour + startTimes[3].Minute / 60;
                LateEvening = startTimes[4].Hour + startTimes[4].Minute / 60;
                Overnight = startTimes[5].Hour + startTimes[5].Minute / 60;
                Weekend_EarlyMorning = startTimes[6].Hour + startTimes[6].Minute / 60;
                Weekend_Morning = startTimes[7].Hour + startTimes[7].Minute / 60;
                Weekend_Afternoon = startTimes[8].Hour + startTimes[8].Minute / 60;
                Weekend_EarlyEvening = startTimes[9].Hour + startTimes[9].Minute / 60;
                Weekend_LateEvening = startTimes[10].Hour + startTimes[10].Minute / 60;
                Weekend_Overnight = startTimes[11].Hour + startTimes[11].Minute / 60;
            }
        }
    }

    //defines special date ranges that are witin special dates
    public class dayTypeDefinition
    {
        public Tuple<DateTime, DateTime> specialDateRanges1 { get; set; }
        public Tuple<DateTime, DateTime> specialDateRanges2 { get; set; }
        public dayTypeDefinition()//no definition
        {
            specialDateRanges1 = new Tuple<DateTime, DateTime>(default(DateTime), default(DateTime));
            specialDateRanges2 = new Tuple<DateTime, DateTime>(default(DateTime), default(DateTime));
        }
        public dayTypeDefinition(DateTime periodOneBegin, DateTime periodOneEnd, DateTime periodTwoBegin, DateTime periodTwoEnd)
        {
            specialDateRanges1 = new Tuple<DateTime, DateTime>(periodOneBegin, periodOneEnd);
            specialDateRanges2 = new Tuple<DateTime, DateTime>(periodTwoBegin, periodTwoEnd);
        }
    }

    //enum integer data for time period based on TTC summary
    public enum timePeriodType
    {
        MorningPeak,
        Midday,
        AfternoonPeak,
        EarlyEvening,
        LateEvening,
        Overnight,
        Weekend_EarlyMorning,
        Weekend_Morning,
        Weekend_Afternoon,
        Weekend_EarlyEvening,
        Weekend_LateEvening,
        Weekend_Overnight,
        empty,
        /*
         * FROM TTC SERVICE SUMMARY:
         * MONDAY TO FRIDAY 
         * 0: Morning peak period ............................. 600a to 900a 
         * 1: Midday ................................................. 900a to 300p 
         * 2: Afternoon peak period .......................... 300p to 700p 
         * 3: Early evening ...................................... 700p to 1000p 
         * 4: Late evening ....................................... 1000p to 100a 
         * 5: Overnight ............................................... 130a to 530a 
         * SATURDAY and SUNDAY
         * 6: Early morning ...................................... 600a to 800a
         * 7: Morning ...................................... 800a to 1200noon
         * 8: Afternoon ................................... 1200noon to 700p
         * 
         * 9: Early evening .....................................700p to 1000p
         * 10: Late evening ...................................... 1000p to 100a
         * 11: Overnight ................... 130a to 530a (900a Sundays) 
         */
    }

    #endregion
}
