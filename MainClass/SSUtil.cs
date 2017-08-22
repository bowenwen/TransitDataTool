using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SQLite;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace MainClass
{
    #region SSim OBJECT CLASSES - Utility Function Classes
    /*
     * UTILITY CLASS OBJECTS
     */
    [Serializable]
    public class DateRange
    {
        public DateTime start { get; set; }
        public DateTime end { get; set; }

        public double getDurationHours()
        {
            return end.Subtract(start).TotalHours;
        }
        public double getDurationSeconds()
        {
            return end.Subtract(start).TotalSeconds;
        }
    }

    public class GeoLocation
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public long Time { get; set; }//optional
        public double DistanceFromStartOfShape { get; set; }//optional

        public GeoLocation(double lat, double lon)
        {
            Latitude = lat;
            Longitude = lon;
        }
        public GeoLocation(double lat, double lon, long epochTime, double DistFromShapeStart)
        {
            Latitude = lat;
            Longitude = lon;
            Time = epochTime;
            DistanceFromStartOfShape = DistFromShapeStart;
        }
        public GeoLocation() { }
    }

    public class VelocityVector
    {
        /// <summary>
        /// Unit: km/hr
        /// </summary>
        public double Speed { get; set; }
        /// <summary>
        /// Unit: degree (360)
        /// </summary>
        public double Bearing { get; set; }//approx
        public GeoLocation StartGPS { get; set; }
        public GeoLocation EndGPS { get; set; }
        /// <summary>
        /// Unit: meters
        /// </summary>
        public double Distance { get; set; }//Shape Distance!
        /// <summary>
        /// Unit: seconds
        /// </summary>
        public double DeltaTime { get; set; }

        public VelocityVector() { }
        /// <summary>
        /// Recommended Constructor: Ensure the GeoLocation Objects have correct Times for proper speed calculations
        /// </summary>
        /// <param name="startPt"></param>
        /// <param name="endPt"></param>
        public VelocityVector(GeoLocation startPt, GeoLocation endPt)
        {
            StartGPS = startPt;
            EndGPS = endPt;
            Distance = EndGPS.DistanceFromStartOfShape - StartGPS.DistanceFromStartOfShape;//SSUtil.GetGlobeDistance(startPt.Latitude, endPt.Latitude, startPt.Longitude, endPt.Longitude);
            DeltaTime = EndGPS.Time - StartGPS.Time;
            Speed = Math.Round((Distance / DeltaTime) * 3.6, 1);
            Bearing = SSUtil.GetBearing(startPt.Latitude, endPt.Latitude, startPt.Longitude, endPt.Longitude); ;
        }
        ///// <summary>
        ///// Determines if the bearing of current vector is of the "same Direction" as destination Heading
        ///// </summary>
        ///// <param name="destiHeading"></param>
        ///// <returns></returns>
        //public bool SameDirectionAs(double destiHeading)//double vehHeading,//bearingTolerance
        //{
        //    return SSUtil.IsSameDirectionAs(this.Bearing, destiHeading);
        //}
        ///// <summary>
        ///// Determines if the bearing of current vector is of the "same Direction" as destination Heading
        ///// </summary>
        ///// <param name="antherVect"></param>
        ///// <returns></returns>
        //public bool SameDirectionAs(VelocityVector antherVect)//double vehHeading,//bearingTolerance
        //{
        //    return SSUtil.IsSameDirectionAs(this.Bearing, antherVect.Bearing);
        //}
    }

    /*
     * UTILITY FUNCTIONS
     */
    public static class SSUtil
    {
        /// <summary>
        /// Input vectors must have coordinates with time. This method calculates the average velocity vector using the total distance and time elapsed.
        /// Output vector has only speed and bearing.
        /// </summary>
        /// <param name="allVectors"></param>
        /// <returns></returns>
        public static VelocityVector VelocityVectorAverage(List<VelocityVector> allVectors)
        {
            VelocityVector finalVect = new VelocityVector();
            double x = 0; //cumulative sum in x
            double y = 0; //cumulative sum in y
            double d = 0; //cumulative sum in distance
            double dt = 0; //cumulative sum in time
            //http://stackoverflow.com/questions/491738/how-do-you-calculate-the-average-of-a-set-of-circular-data
            //x = y = 0
            //foreach angle {
            //    x += cos(angle)
            //    y += sin(angle)
            //}
            //average_angle = atan2(y, x)
            foreach (VelocityVector aVector in allVectors)
            {
                //average speed
                d += aVector.Distance;
                dt += aVector.DeltaTime;
                //average bearing
                x += Math.Cos(aVector.Bearing) * aVector.Distance;//weighted by distance
                y += Math.Sin(aVector.Bearing) * aVector.Distance;//weighted by distance
            }
            finalVect.Speed = Math.Round((d / dt) * 3.6, 1);
            finalVect.Bearing = Math.Round(Math.Atan2((y / d), (x / d)), 1);//weighted by distance
            finalVect.Distance = d;
            finalVect.DeltaTime = dt;

            return finalVect;
        }
        /// <summary>
        /// Determines if the bearing of one Heading is of the "same Direction" as destination Heading
        /// </summary>
        /// <param name="vehHeading"></param>
        /// <param name="destiHeading"></param>
        /// <returns></returns>
        //public static bool IsSameDirectionAs(double vehHeading, double destiHeading)//bearingTolerance
        //{
        //    //http://stackoverflow.com/questions/16180595/find-the-angle-between-two-bearings
        //    double a1 = vehHeading;
        //    double a2 = destiHeading;
        //    double angleDiff = Math.Min((a1 - a2) < 0 ? a1 - a2 + 360 : a1 - a2, (a2 - a1) < 0 ? a2 - a1 + 360 : a2 - a1);
        //    //determine if the bearing deviation is acceptable (current stop to GPS point versus current stop to next stop)
        //    double totalBearingDeviation = 180;//in deg, up to 180 deg turns
        //    if ((angleDiff) <= (totalBearingDeviation))
        //        return true;//destination position is in the same general Direction as veh Heading
        //    else
        //        return false;
        //}
        /// <summary>
        /// Initialize an array with a particular value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <param name="value"></param>
        public static void Populate<T>(this T[] arr, T value)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }
        }
        public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> items,
                  Func<T, TKey> keyer)
        {
            var set = new HashSet<TKey>();
            var list = new List<T>();
            foreach (var item in items)
            {
                var key = keyer(item);
                if (set.Contains(key))
                    continue;
                list.Add(item);
                set.Add(key);
            }
            return list;
        }
        /// <summary>
        /// Check if a list contains any duplicates
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static bool ContainsDuplicates<T>(this IEnumerable<T> enumerable)
        {
            var knownKeys = new HashSet<T>();
            return enumerable.Any(item => !knownKeys.Add(item));
        }
        /// <summary>
        /// Check if a list contains any duplicates, if it does, returns the indices of all the first duplicate occurance. The list of indices is in ascending order.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static List<int> DuplicateFirstIndices<T>(this List<T> enumerable)
        {
            var results = new List<int>();
            if (enumerable.ContainsDuplicates())
            {
                var duplicates = enumerable
                    .GroupBy(i => i)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);
                foreach (var d in duplicates)
                    results.Add(enumerable.IndexOf(d));

                //results.OrderByDescending(x => x).ToList();
                return results;
            }
            else
            {
                return results;//no duplicate
            }
        }
        /// <summary>
        /// Check if a list contains any duplicates, if it does, returns all the values of duplicate and their indices
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static List<Tuple<T, List<int>>> DuplicateAllValuesAndIndices<T>(this List<T> enumerable)
        {
            var results = new List<Tuple<T, List<int>>>();

            if (enumerable.ContainsDuplicates())
            {
                var duplicatesWithIndices = enumerable
                    // Associate each name/value with an index
                    .Select((Name, Index) => new { Name, Index })
                    // Group according to name
                    .GroupBy(x => x.Name)
                    // Only care about Name -> {Index1, Index2, ..}
                    .Select(xg => new
                    {
                        Name = xg.Key,
                        Indices = xg.Select(x => x.Index)
                    })
                    // And groups with more than one index represent a duplicate key
                    .Where(x => x.Indices.Count() > 1);

                // Now, duplicatesWithIndices is typed like:
                //   IEnumerable<{Name:string,Indices:IEnumerable<int>}>

                // Let's say we print out the duplicates (the ToArray is for .NET 3.5):
                foreach (var g in duplicatesWithIndices)
                {
                    results.Add(new Tuple<T, List<int>>(g.Name, g.Indices.ToList()));//All values

                    //Console.WriteLine("Have duplicate " + g.Name + " with indices " + string.Join(",", g.Indices.ToArray()));
                }

                return results;
            }
            else
            {
                return results;//no duplicate
            }
        }
        /// <summary>
        /// Check if a list contains consecutive duplicates, if it does, returns all but the last duplicate's index.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static List<int> DuplicateAllButFirstIndices<T>(this List<T> enumerable)
        {
            var results = new List<int>();
            if (enumerable.ContainsDuplicates())
            {
                //get all the duplicates' name and indices
                List<Tuple<T, List<int>>> duplicatesWithIndices = enumerable.DuplicateAllValuesAndIndices();

                foreach (var g in duplicatesWithIndices)
                {
                    int lastIndex = g.Item2[0];
                    for (int i = 1; i < g.Item2.Count; i++)
                    {
                        results.Add(g.Item2[i]);//all but first
                        //if ((g.Item2[i] - lastIndex) == 1)//consecutive duplicates only case
                        //{
                        //    results.Add(lastIndex);
                        //    lastIndex = g.Item2[i];
                        //}
                    }
                }

                //results.OrderByDescending(x => x).ToList();
                return results;
            }
            else
            {
                return results;//no duplicate
            }
        }
        /// <summary>
        /// Get a list of indices of this list that intersects with another (second) given list.
        /// Note the indices are in reverse numeric order for easier removal.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumerable"></param>
        /// <param name="secondList"></param>
        /// <returns></returns>
        public static List<int> GetIntersectIndices<T>(this IEnumerable<T> enumerable, IEnumerable<T> secondList)
        {
            var matchIndices = new List<int>();
            var secondListHash = new HashSet<T>(secondList);
            matchIndices.AddRange(enumerable
             .Select((v, i) => new { Index = i, Value = v })
             .Where(x => secondListHash.Contains(x.Value))
             .Select(x => x.Index)
             .ToList());
            matchIndices.Sort((item1, item2) => -1 * item1.CompareTo(item2)); // descending sort
            //http://stackoverflow.com/questions/3062513/how-can-i-sort-generic-list-desc-and-asc
            return matchIndices;
        }
        public static List<T> Replace<T>(this List<T> enumerable, T oldItem, T newItem)
        {
            for (int i = 0; i < enumerable.Count(); i++)
            {
                if (enumerable[i].Equals(oldItem))
                {
                    enumerable[i] = newItem;
                }
            }
            return enumerable;
        }
        public static TimeSpan GetUpTime()//using System.Runtime.InteropServices; 
        {
            return TimeSpan.FromMilliseconds(GetTickCount64());
        }
        [DllImport("kernel32.dll")]
        extern static UInt64 GetTickCount64();
        //Time Keeping methods:http://stackoverflow.com/questions/2883576/how-do-you-convert-epoch-time-in-c
        public static DateTime EpochTimeToUTCDateTime(this long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            epoch = epoch.AddSeconds(unixTime);
            return epoch.ToUniversalTime();//local time
        }
        public static DateTime EpochTimeToLocalDateTime(this long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            epoch = epoch.AddSeconds(unixTime);
            return epoch.ToLocalTime();//local time
        }
        public static double EpochToLOCALSecondsFromMidnight(this long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            epoch = epoch.AddSeconds(unixTime);
            long localMidnightEpoch = epoch.Date.DateTimeToEpochTime();
            return Convert.ToDouble(unixTime - localMidnightEpoch);//local time
        }
        public static long DateTimeToEpochTime(this DateTime date)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Convert.ToInt64((date.ToUniversalTime() - epoch).TotalSeconds);
            //Console.WriteLine(Convert.ToInt64((date.ToUniversalTime() - epoch).TotalSeconds));
        }
        public static TimeSpan DateTimeToTimeSpanFromLastLocalMidnight(this DateTime dateTime)
        {
            DateTime date = dateTime.ToLocalTime().Date;
            return TimeSpan.FromTicks(dateTime.ToLocalTime().Subtract(date).Ticks);
            //Console.WriteLine(Convert.ToInt64((date.ToUniversalTime() - epoch).TotalSeconds));
        }
        public static long CurrentEpochTime()
        {
            DateTime date = DateTime.UtcNow;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Convert.ToInt64((date.ToUniversalTime() - epoch).TotalSeconds);
        }
        public static string DateTimeNamingFormat(this DateTime date, bool excludeTime = false, bool excludeMinsSecs = false)
        {
            string dateString = "";
            if (excludeTime)
            {
                dateString = date.ToString("yyyyMMdd");
            }
            else if (excludeMinsSecs)
            {
                dateString = date.ToString("yyyyMMddHH");
            }
            else
            {
                dateString = date.ToString("yyyyMMddHHmmss");
            }
            return dateString;
            //Console.WriteLine(Convert.ToInt64((date.ToUniversalTime() - epoch).TotalSeconds));
        }
        public static string DateTimeISO8601Format(this DateTime date, bool dateOnly = false)
        {
            if (dateOnly)
                return date.ToString("yyyy-MM-dd");
            else
                return date.ToString("yyyy-MM-dd HH:mm:ss");
        }
        public static string EpochToDateTimeISO8601Format(this long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            epoch = epoch.AddSeconds(unixTime);
            return epoch.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            //all date time outputs are in UTC time, -5 to get EST, and -4 to get EDT
        }
        public static string EpochToLOCALDateTimeISO8601Format(this long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            epoch = epoch.AddSeconds(unixTime);
            return epoch.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            //all date time outputs are in UTC time, -5 to get EST, and -4 to get EDT
        }
        public static string timeofDayFormat(this DateTime date)
        {
            string dateString = date.ToString("HH:mm:ss");
            return dateString;
        }
        public static int numDistinctItems(this List<int> listOfInts)
        {
            List<int> lst = listOfInts;
            int res = (from x in lst
                       select x).Distinct().Count();
            return res;
        }
        public static int numDistinctItems(this List<string> listOfStrings)
        {
            List<string> lst = listOfStrings;
            int res = (from x in lst
                       select x).Distinct().Count();
            return res;
        }
        public static List<int> stringToIntegerList(this List<string> listOfStrings)
        {
            List<string> lst = listOfStrings;
            List<string> distinct = new List<string>();//distinct elements
            List<int> final = new List<int>();
            int n = numDistinctItems(listOfStrings);

            for (int i = 0; i < lst.Count; i++)
            {
                if (distinct.Count == 0)
                {
                    distinct.Add(lst[i]);
                    final.Add(0);
                }
                else
                {
                    int m = distinct.Count;
                    for (int j = 0; j < m; j++)
                    {
                        if (lst[i].Equals(distinct[j]))
                        {
                            final.Add(j);
                            break;
                        }
                    }
                    if (final.Count <= i)
                    {
                        distinct.Add(lst[i]);
                        final.Add(distinct.IndexOf(lst[i]));
                    }
                }
            }
            return final;
            /* Testing passed: 
             *             List<string> testString = new List<string> { "abc", "cba", "abc", "cba", "xyzxx", "cba", "cba", "abc", "xyzxx" };
             *             List<int> testInts = SSimUtility.stringToIntegerList(testString);
             */
        }
        public static int GetTableMaxID(string dbFile, string tableName, string columnName)
        {
            string sql = string.Format("SELECT Max({0}) FROM {1}", columnName, tableName);

            int count = 0;
            //try
            //{
            if (File.Exists(dbFile))
            {
                using (SQLiteConnection dbConn = new SQLiteConnection("Data Source=" + dbFile + "; Cache Size=10000; Page Size=4096"))
                {
                    using (SQLiteCommand cmdCount = new SQLiteCommand(sql, dbConn))
                    {
                        dbConn.Open();
                        string countResult = "" + cmdCount.ExecuteScalar();
                        if (countResult == "")
                        {
                            return 0;
                        }
                        else
                        {
                            count = Convert.ToInt32(countResult);
                        }
                        dbConn.Close();
                    }
                }
            }
            else
            {
                return 0;
            }

            return count;

            //}
            //catch (Exception e)
            //{
            //    return 0;
            //}
        }
        public static int GetTableMaxID(SQLiteConnection dbConn, string tableName, string columnName)
        {
            string sql = string.Format("SELECT Max({0}) FROM {1}", columnName, tableName);

            int count = 0;
            //try
            //{
            using (SQLiteCommand cmdCount = new SQLiteCommand(sql, dbConn))
            {
                //dbConn.Open();
                string countResult = "" + cmdCount.ExecuteScalar();
                if (countResult == "")
                {
                    return 0;
                }
                else
                {
                    count = Convert.ToInt32(countResult);
                }
                //dbConn.Close();
            }
            return count;
            //}
            //catch (Exception e)
            //{
            //    return 0;
            //}
        }
        public static int GetTableSize(string dbFile, string tableName)
        {
            string sql = string.Format("SELECT COUNT(*) FROM {0}", tableName);

            int count = 0;
            //try
            //{
            if (File.Exists(dbFile))
            {
                using (SQLiteConnection dbConn = new SQLiteConnection("Data Source=" + dbFile + "; Cache Size=10000; Page Size=4096"))
                {
                    using (SQLiteCommand cmdCount = new SQLiteCommand(sql, dbConn))
                    {
                        dbConn.Open();
                        string countResult = "" + cmdCount.ExecuteScalar();
                        if (countResult == "")
                        {
                            return 0;
                        }
                        else
                        {
                            count = Convert.ToInt32(countResult);
                        }
                        dbConn.Close();
                    }
                }
            }
            else
            {
                return 0;
            }

            return count;
            //}
            //catch (Exception e)
            //{
            //    return 0;
            //}
        }
        public static int GetTableSize(SQLiteConnection dbConn, string tableName)
        {
            string sql = string.Format("SELECT COUNT(*) FROM {0}", tableName);

            int count = 0;
            //try
            //{
            using (SQLiteCommand cmdCount = new SQLiteCommand(sql, dbConn))
            {
                //dbConn.Open();
                string countResult = "" + cmdCount.ExecuteScalar();
                if (countResult == "")
                {
                    return 0;
                }
                else
                {
                    count = Convert.ToInt32(countResult);
                }
                //dbConn.Close();
            }
            return count;
            //}
            //catch (Exception e)
            //{
            //    return 0;
            //}
        }
        //from GTFSConvert
        //http://www.movable-type.co.uk/scripts/latlong.html
        /// <summary>
        /// Calculates the distance between two GPS points, returns meters
        /// Uses the great circle method: https://en.wikipedia.org/wiki/Great-circle_distance
        /// </summary>
        /// <param name="lat1"></param>
        /// <param name="lon1"></param>
        /// <param name="lat2"></param>
        /// <param name="lon2"></param>
        /// <returns>Distance in meters</returns>
        public static double GetGlobeDistance(double lat1, double lat2, double lon1, double lon2)        //returns distance in m
        {
            //haversine formula
            const double radiusEarthKilometres = 6372.8; // In kilometers
            var dLat = DegreeToRadian(lat2 - lat1);
            var dLon = DegreeToRadian(lon2 - lon1);
            lat1 = DegreeToRadian(lat1);
            lat2 = DegreeToRadian(lat2);

            var v1 = Math.Sin(dLat / 2);
            var v2 = Math.Sin(dLon / 2);

            var a = v1 * v1 + v2 * v2 * Math.Cos(lat1) * Math.Cos(lat2);
            var c = 2 * Math.Asin(Math.Sqrt(a));
            return radiusEarthKilometres * c * 1000;//in meters

            //special case of the Vincenty formula for an ellipsoid - has rounding problems
            //var R = 6371; // In kilometers
            //var dLat = Math.PI * (lat2 - lat1) / 180.0;
            //var dLon = Math.PI * (lon2 - lon1) / 180.0;
            //lat1 = Math.PI * lat1 / 180.0;
            //lat2 = Math.PI * lat2 / 180.0;
            //var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
            ////var c = 2 * Math.Asin(Math.Sqrt(a));
            ////return R * 2 * Math.Asin(Math.Sqrt(a)) * 1000;
            //return Math.Abs(R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * 1000);//Bo Fix
        }
        // find a new GPS location from start point and start bearing
        // from http://stackoverflow.com/questions/3225803/calculate-endpoint-given-distance-bearing-starting-point
        public static GeoLocation FindPointAtDistanceFrom(GeoLocation startPoint, double initialBearingRadians, double distanceMetres)
        {
            const double radiusEarthKilometres = 6372.8; //6371.01;
            var distRatio = (distanceMetres / 1000) / radiusEarthKilometres;
            var distRatioSine = Math.Sin(distRatio);
            var distRatioCosine = Math.Cos(distRatio);

            var startLatRad = DegreeToRadian(startPoint.Latitude);
            var startLonRad = DegreeToRadian(startPoint.Longitude);

            var startLatCos = Math.Cos(startLatRad);
            var startLatSin = Math.Sin(startLatRad);

            var endLatRads = Math.Asin((startLatSin * distRatioCosine) + (startLatCos * distRatioSine * Math.Cos(initialBearingRadians)));

            var endLonRads = startLonRad
                + Math.Atan2(
                    Math.Sin(initialBearingRadians) * distRatioSine * startLatCos,
                    distRatioCosine - startLatSin * Math.Sin(endLatRads));

            return new GeoLocation
            {
                Latitude = RadianToDegree(endLatRads),
                Longitude = RadianToDegree(endLonRads)
            };
        }
        public static double GetDistFromPtToLine(GeoLocation lineStart, GeoLocation lineEnd, GeoLocation point)
        {
            // http://stackoverflow.com/questions/7803004/distance-from-point-to-line-on-earth
            //find triangle area
            var d0 = GetGlobeDistance(point.Latitude, lineStart.Latitude, point.Longitude, lineStart.Longitude);
            var d1 = GetGlobeDistance(lineStart.Latitude, lineEnd.Latitude, lineStart.Longitude, lineEnd.Longitude);
            var d2 = GetGlobeDistance(lineEnd.Latitude, point.Latitude, lineEnd.Longitude, point.Longitude);
            var halfP = (d0 + d1 + d2) * 0.5;//half of parameter
            var area = Math.Sqrt(halfP * (halfP - d0) * (halfP - d1) * (halfP - d2));
            double distanceToLine = 2 * area / d1;
            return distanceToLine;

            ////http://gis.stackexchange.com/questions/102014/point-to-line-calculations/184695#184695
            ////https://github.com/bmwcarit/barefoot
            ////http://math.stackexchange.com/questions/993236/calculating-a-perpendicular-distance-to-a-line-when-using-coordinates-latitude
            ////Chosen approach:
            ////http://stackoverflow.com/questions/3120357/get-closest-point-to-a-line
            ////http://stackoverflow.com/questions/849211/shortest-distance-between-a-point-and-a-line-segment
            ////http://stackoverflow.com/questions/16266809/convert-from-latitude-longitude-to-x-y
            ////(Modified to not need C# XNA)
            ////uses equirectangular projection - only an approximations - not exact
            //var R = 6372.8; // In kilometers
            //double cosPhi = 0.72353122715817442; // Math.Cos((43.6532 * Math.PI / 180.0)) or  Cos(Phi(lat0)) = 43.6532
            //double[] pointA = new double[2];
            //pointA[0] = R * lineStart.Longitude * cosPhi * Math.PI / 180.0;  //lon1 ~ x
            //pointA[1] = R * lineStart.Latitude * Math.PI / 180.0;  //lat1 ~ y
            //double[] pointB = new double[2];
            //pointB[0] = R * lineEnd.Longitude * cosPhi * Math.PI / 180.0;  //lon1 ~ x
            //pointB[1] = R * lineEnd.Latitude * Math.PI / 180.0;  //lat1 ~ y
            //double[] pointC = new double[2];
            //pointC[0] = R * point.Longitude * cosPhi * Math.PI / 180.0;  //lon1 ~ x
            //pointC[1] = R * point.Latitude * Math.PI / 180.0;  //lat1 ~ y

            //double d1 = pointA[0] - pointB[0];
            //double d2 = pointA[1] - pointB[1];
            ////double ABdist = Math.Sqrt(d1 * d1 + d2 * d2);  //length
            //double magnitudeAB = d1 * d1 + d2 * d2;     //Magnitude of AB vector (it's length squared)    

            ////find dot product
            //double[] AB = new double[2];
            //double[] BC = new double[2];
            //AB[0] = pointB[0] - pointA[0];
            //AB[1] = pointB[1] - pointA[1];
            //BC[0] = pointC[0] - pointB[0];
            //BC[1] = pointC[1] - pointB[1];
            //double ABAPproduct = AB[0] * BC[0] + AB[1] * BC[1];    //The DOT product of a_to_p and a_to_b     
            //double distance = ABAPproduct / magnitudeAB; //The normalized "distance" from a to your closest point  

            //return Math.Abs(distance) * 1000;//km to m

            ////if (distance < 0)     //Check if P projection is over vectorAB     
            ////{
            ////    return A;
            ////}
            ////else if (distance > 1)
            ////{
            ////    return B;
            ////}
            ////else
            ////{
            ////    return A + AB * distance;
            ////}
        }
        /// <summary>
        /// Calculates the Heading or bearing from (lat1,lon1) to (lat2,lon2), returns degrees (360). 
        /// Note that bearing is the degree measured clockwise from the true north line.
        /// http://stackoverflow.com/questions/2042599/direction-between-2-latitude-longitude-points-in-c-sharp
        /// </summary>
        /// <param name="lat1"></param>
        /// <param name="lat2"></param>
        /// <param name="lon1"></param>
        /// <param name="lon2"></param>
        /// <returns></returns>
        public static double GetBearing(double lat1, double lat2, double lon1, double lon2, bool returnDegree = true)
        {
            //lat1 and lon1 are for starting point, lat2 and lon2 are for ending point
            //Great Circle Method - non-constant heading
            lat1 = DegreeToRadian(lat1);
            lon1 = DegreeToRadian(lon1);
            lat2 = DegreeToRadian(lat2);
            lon2 = DegreeToRadian(lon2);
            double deltaLong = lon2 - lon1;
            double y = Math.Sin(deltaLong) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                    Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLong);
            double radianBearing = Math.Atan2(y, x);

            ////Rhumb Line Method
            //var dLon = DegreeToRadian(lon2 - lon1);
            //var dPhi = Math.Log(Math.Tan(DegreeToRadian(lat2) / 2 + Math.PI / 4) / Math.Tan(DegreeToRadian(lat1) / 2 + Math.PI / 4));
            //if (Math.Abs(dLon) > Math.PI)
            //    dLon = dLon > 0 ? -(2 * Math.PI - dLon) : (2 * Math.PI + dLon);
            //double radianBearing = Math.Atan2(dLon, dPhi);

            if (returnDegree)
            {
                return BearingRadianToDegree(radianBearing);
            }
            else
            {
                return radianBearing;
            }
        }
        public static double BearingRadianToDegree(double radianBearing)
        {
            return (RadianToDegree(radianBearing) + 360) % 360;
        }
        /// <summary>
        /// Gets the difference in the angle between two bearings, and output direction of bearing
        /// http://stackoverflow.com/questions/16180595/find-the-angle-between-two-bearings
        /// </summary>
        /// <param name="a1"></param>
        /// <param name="a2"></param>
        /// <returns>the difference in bearing angle, and the direction (whether it is anticlockwise dir from bearing0 to bearing1)</returns>
        public static double GetDegBearingDiff(double bearing0, double bearing1, out bool is_anticlock_dir)
        {
            //Method 1: difference only, no direction (double a1, double a2)
            //return Math.Min((a1 - a2) < 0 ? a1 - a2 + 360 : a1 - a2, (a2 - a1) < 0 ? a2 - a1 + 360 : a2 - a1);

            //Method 2: differences and direction (double bearing0, double bearing1)
            double maxBearing = Math.Max(bearing0, bearing1);
            double minBearing = Math.Min(bearing0, bearing1);
            double firstDir = maxBearing - minBearing;
            double secondDir = minBearing + 360 - maxBearing;
            double diff = Math.Min(firstDir, secondDir);
            bool anticlock_dir = false;
            double anticlock = ((bearing1 + diff) >= 360) ? (bearing1 + diff - 360) : (bearing1 + diff);
            if (anticlock == bearing0)
            {
                anticlock_dir = true;
            }
            is_anticlock_dir = anticlock_dir;
            return diff;
        }
        public static double DegreeToRadian(double angle)
        {
            return Math.PI * angle / 180.0;
        }
        public static double RadianToDegree(double radians)
        {
            return radians * 180 / Math.PI;//Math.PI
        }
        public static GeoLocation GetGeoMidPoint(GeoLocation posA, GeoLocation posB)
        {
            GeoLocation midPoint = new GeoLocation();

            double dLon = DegreeToRadian(posB.Longitude - posA.Longitude);
            double Bx = Math.Cos(DegreeToRadian(posB.Latitude)) * Math.Cos(dLon);
            double By = Math.Cos(DegreeToRadian(posB.Latitude)) * Math.Sin(dLon);

            midPoint.Latitude = RadianToDegree(Math.Atan2(
                         Math.Sin(DegreeToRadian(posA.Latitude)) + Math.Sin(DegreeToRadian(posB.Latitude)),
                         Math.Sqrt(
                             (Math.Cos(DegreeToRadian(posA.Latitude)) + Bx) *
                             (Math.Cos(DegreeToRadian(posA.Latitude)) + Bx) + By * By)));

            midPoint.Longitude = posA.Longitude + RadianToDegree(Math.Atan2(By, Math.Cos(DegreeToRadian(posA.Latitude)) + Bx));

            return midPoint;
        }
        public static GeoLocation GetCentralGeoCoordinate(IList<GeoLocation> geoCoordinates)
        {
            //http://stackoverflow.com/questions/6671183/calculate-the-center-point-of-multiple-Latitude-Longitude-coordinate-pairs
            if (geoCoordinates.Count == 1)
            {
                return geoCoordinates.Single();
            }

            double x = 0;
            double y = 0;
            double z = 0;

            foreach (var geoCoordinate in geoCoordinates)
            {
                var Latitude = geoCoordinate.Latitude * Math.PI / 180;
                var Longitude = geoCoordinate.Longitude * Math.PI / 180;

                x += Math.Cos(Latitude) * Math.Cos(Longitude);
                y += Math.Cos(Latitude) * Math.Sin(Longitude);
                z += Math.Sin(Latitude);
            }

            var total = geoCoordinates.Count;

            x = x / total;
            y = y / total;
            z = z / total;

            var centralLongitude = Math.Atan2(y, x);
            var centralSquareRoot = Math.Sqrt(x * x + y * y);
            var centralLatitude = Math.Atan2(z, centralSquareRoot);

            return new GeoLocation(centralLatitude * 180 / Math.PI, centralLongitude * 180 / Math.PI);
        }
        public static int RoundOff(this int i, int multiplesOf = 10)
        {
            return ((int)Math.Round(i / multiplesOf * 1.0)) * multiplesOf;
        }
        public static double StandardDeviation_P(this IEnumerable<double> values)
        {
            if (values.Count() < 1)
            {
                return 0;
            }
            double avg = values.Average();
            return Math.Sqrt(values.Average(v => Math.Pow(v - avg, 2)));
        }
        public static double StandardDeviation_S(this IEnumerable<double> values)
        {
            if (values.Count() < 2)
            {
                return 0;
            }
            //Compute the Average      
            double avg = values.Average();
            //Perform the Sum of (value-avg)_2_2      
            double sum = values.Sum(d => Math.Pow(d - avg, 2));
            //Put it all together      
            return Math.Sqrt((sum) / (values.Count() - 1));
        }
        /// <summary>
        /// Removes any values in the list that are outside of a two-tailed p value range.
        /// Does not remove any outliers if number of value is less or equal to 3.
        /// </summary>
        /// <param name="values"></param>
        /// <param name="pVal"></param>
        /// <returns>list with no outliers</returns>
        public static IEnumerable<double> removeOutliers_NormalDist(this IEnumerable<double> values, double pVal = 0.05)
        {
            List<double> outliers = new List<double>();
            if (values.Count() > 3)
            {
                MathNet.Numerics.Distributions.Normal distribution = MathNet.Numerics.Distributions.Normal.Estimate(values);
                foreach (double val in values)
                {
                    double cur_p = distribution.CumulativeDistribution(val);
                    if (cur_p < (pVal / 2) || cur_p > (1 - (pVal / 2)))
                    {
                        outliers.Add(val);
                    }
                }
            }
            return values.Except(outliers);
        }
        #region XML Serializer Classes
        /// <summary>
        /// Serializes an object.
        /// Ref: http://stackoverflow.com/questions/6115721/how-to-save-restore-serializable-object-to-from-file
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="serializableObject"></param>
        /// <param name="fileName"></param>
        public static void SerializeXMLObject<T>(T serializableObject, string fileName)
        {
            if (serializableObject == null) { return; }

            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                XmlSerializer serializer = new XmlSerializer(serializableObject.GetType());
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.Serialize(stream, serializableObject);
                    stream.Position = 0;
                    xmlDocument.Load(stream);
                    xmlDocument.Save(fileName);
                    stream.Close();
                }
            }
            catch (Exception ex)
            {
                //Log exception here
                //LogUpdateEventArgs args = new LogUpdateEventArgs();
                //args.logMessage = String.Format("Exception serializing file: {0}.", fileName);
                //OnLogUpdate(args);
            }
        }
        /// <summary>
        /// Deserializes an xml file into an object list
        /// Ref: http://stackoverflow.com/questions/6115721/how-to-save-restore-serializable-object-to-from-file
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static T DeSerializeXMLObject<T>(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) { return default(T); }

            T objectOut = default(T);

            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(fileName);
                string xmlString = xmlDocument.OuterXml;

                using (StringReader read = new StringReader(xmlString))
                {
                    Type outType = typeof(T);

                    XmlSerializer serializer = new XmlSerializer(outType);
                    using (XmlReader reader = new XmlTextReader(read))
                    {
                        objectOut = (T)serializer.Deserialize(reader);
                        reader.Close();
                    }

                    read.Close();
                }
            }
            catch (Exception ex)
            {
                //Log exception here
                //LogUpdateEventArgs args = new LogUpdateEventArgs();
                //args.logMessage = String.Format("Exception deserializing file: {0}.", fileName);
                //OnLogUpdate(args);
            }

            return objectOut;
        }
        #endregion
    }
    /// <summary>
    /// Reference Article http://www.codeproject.com/KB/tips/SerializedObjectCloner.aspx
    /// Provides a method for performing a deep copy of an object.
    /// Binary Serialization is used to perform the copy.
    /// </summary>
    public static class ObjectCopier
    {
        /// <summary>
        /// Perform a deep Copy of the object, can handle any serializable classes, including list and array.
        /// </summary>
        /// <typeparam name="T">The type of object being copied.</typeparam>
        /// <param name="source">The object instance to copy.</param>
        /// <returns>The copied object.</returns>
        public static T Clone<T>(T source)
        {
            if (!typeof(T).IsSerializable)
            {
                throw new ArgumentException("The type must be serializable.", "source");
            }

            // Don't serialize a null object, simply return the default for that object
            if (Object.ReferenceEquals(source, null))
            {
                return default(T);
            }

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new MemoryStream();
            using (stream)
            {
                formatter.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T)formatter.Deserialize(stream);
            }
        }
    }
    /// <summary>
    /// Read from and write to binary files 
    /// </summary>
    public static class BinaryFileHandler
    {
        public static void WriteFile<T>(T source, string filePath)
        {
            if (!typeof(T).IsSerializable)
            {
                throw new ArgumentException("The type must be serializable.", "source");
            }

            // Don't serialize a null object, simply return the default for that object
            if (Object.ReferenceEquals(source, null))
            {
                //don't write
            }

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using (stream)
            {
                formatter.Serialize(stream, source);
                //stream.Seek(0, SeekOrigin.Begin);
                stream.Close();
            }
        }

        public static T ReadFile<T>(string filePath)
        {
            T returnObject;

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Specified file cannot be found", filePath);
            }

            byte[] tempObj = File.ReadAllBytes(filePath);

            IFormatter formatter = new BinaryFormatter();
            //Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream stream = new MemoryStream(tempObj);
            returnObject = (T)formatter.Deserialize(stream);
            stream.Close();
            tempObj = null;

            return returnObject;
        }
    }
    /// <summary>
    /// Gets the new filename in case of duplicate filename.
    /// Credit: https://stackoverflow.com/questions/1078003/c-how-would-you-make-a-unique-filename-by-adding-a-number.
    /// </summary>
    public static class FileNameHandler
    {
        /// <summary>
        /// if file exist, change the file name to back it up. This prevents overwritting but also make available the original file name.
        /// </summary>
        /// <param name="path"></param>
        public static void BackupThisFilename(string path)
        {
            if (File.Exists(path))
            {
                string newBkName = NextAvailableFilename(path, "_bk{0}");
                try
                {
                    File.Move(path, newBkName);
                }
                catch (IOException)
                {
                    //do nothing, file still under access by program
                }
            }
        }

        /// <summary>
        /// get the next available filename (prevents overwritting)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string NextAvailableFilename(string path, string numberPattern = " ({0})")
        {
            // Short-cut if already available
            if (!File.Exists(path))
                return path;

            // If path has extension then insert the number pattern just before the extension and return next filename
            if (Path.HasExtension(path))
                return GetNextFilename(path.Insert(path.LastIndexOf(Path.GetExtension(path)), numberPattern));

            // Otherwise just append the pattern to the path and return next filename
            return GetNextFilename(path + numberPattern);
        }

        //private static string numberPattern = " ({0})";
        //private static string numberPattern = "_bk{0}";
        private static string GetNextFilename(string pattern)
        {
            string tmp = string.Format(pattern, 1);
            if (tmp == pattern)
                throw new ArgumentException("The pattern must include an index place-holder", "pattern");

            if (!File.Exists(tmp))
                return tmp; // short-circuit if no matches

            int min = 1, max = 2; // min is inclusive, max is exclusive/untested

            while (File.Exists(string.Format(pattern, max)))
            {
                min = max;
                max *= 2;
            }

            while (max != min + 1)
            {
                int pivot = (max + min) / 2;
                if (File.Exists(string.Format(pattern, pivot)))
                    min = pivot;
                else
                    max = pivot;
            }

            return string.Format(pattern, max);
        }
    }
    #endregion
}
