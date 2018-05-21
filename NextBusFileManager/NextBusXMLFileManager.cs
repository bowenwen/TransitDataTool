using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Concurrent;
using System.Threading;
using ServiceStack.Text;

namespace NextBusFileManager
{
    /// <summary>
    /// NextBus XML File Manager for Toronto
    /// - This file manager repackages existing NextBus xml files created by the Transit Data Tool
    /// </summary>
    class NextBusXMLFileManager
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=====================================");
            Console.WriteLine("NextBus XML File Manager for Toronto");
            Console.WriteLine("=====================================");
            string directory = "";
            while (!Directory.Exists(directory))
            {
                Console.WriteLine("=> No directory set. Working directory should contain only the xml files you wish to repackage.\n=> Please set a working directory:");
                directory = Console.ReadLine();
            }
            directory = directory.Last() == '\\' ? directory : directory + "\\";
            string main_request = "l";
            while (!main_request.Equals("q"))
            {
                Console.WriteLine("=> Input request: w - write new file, q - quit program.");
                main_request = Console.ReadLine();
                if (main_request.Equals("w"))
                {
                    //initialize final file
                    Console.WriteLine("=> Enter output filename (no extension):");
                    string newfilename = Console.ReadLine();
                    Console.WriteLine("=> Do you want to specify filter? y - specify filter, n - no filter, save now.");
                    string filter_request = Console.ReadLine();
                    List<String> routeNumbers = new List<string>();
                    List<String> vehNumbers = new List<string>();
                    if (filter_request.Equals("y") || filter_request.Equals("n"))
                    {
                        string filter_option = "route";
                        if (filter_request.Equals("y"))
                        {
                            while (!filter_option.Equals("q") && !filter_option.Equals("w"))
                            {
                                Console.WriteLine("=> Add filter: route - filter by routeTag (route number), vehicle - filter by id (vehicle IDs), w - done and write, q - quit and clear option");
                                filter_option = Console.ReadLine();
                                if (filter_option.Equals("route"))
                                {
                                    Console.WriteLine("=> Enter ALL route number separated by comma:");
                                    string routeString = Console.ReadLine();
                                    routeNumbers.AddRange(routeString.Split(',').ToList()); //Array.ConvertAll()
                                }
                                else if (filter_option.Equals("vehicle"))
                                {
                                    Console.WriteLine("=> Enter ALL vehicle number separated by comma:");
                                    string routeString = Console.ReadLine();
                                    vehNumbers.AddRange(routeString.Split(',').ToList()); //Array.ConvertAll()
                                }
                            }
                        }
                        if (filter_option.Equals("w"))
                        {
                            //go through every file and perform filter on the file, load filtered data
                            Console.WriteLine("=> Processing all xml files in directory");

                            //preparing csv file
                            // more info: https://joshclose.github.io/CsvHelper/writing#writing-a-single-record
                            string csv_filepath = directory + newfilename + ".csv";

                            //string[] currentfilepaths = Directory.GetFiles(directory);//the filenames contain directory path
                            List<string> currentfilepaths = Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories).ToList();
                            int total_count = currentfilepaths.Count();
                            int dataload_count = 0;
                            int processed_count = 0;
                            int write_count = 0;

                            //processing object
                            ConcurrentDictionary<string, List<Vehicle>> unsavedFiles = new ConcurrentDictionary<string, List<Vehicle>>();

                            // ParallelTasks
                            Parallel.Invoke(() =>
                            {
                                //foreach (string filepath in currentfilepaths)
                                Parallel.ForEach(currentfilepaths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, filepath =>
                                {
                                    List<Vehicle> currentFile = new List<Vehicle>();
                                    string filename = filepath.Split('\\').Last();

                                    //===load this file===
                                    currentFile.AddRange(DeSerializeXMLObject<List<Vehicle>>(filepath));
                                    dataload_count++;
                                    Console.Write("\r{1} of {0} loaded, {2} of {0} processed, {3} of {0} written...   ", total_count, dataload_count, processed_count, write_count);

                                    //===process and filter this file===
                                    if (filter_request.Equals("y") && routeNumbers.Count > 0)
                                    {
                                        //contains exacly the route number
                                        currentFile = currentFile.AsParallel().Where(o => routeNumbers.Contains(o.RouteTag)).ToList();
                                    }
                                    if (filter_request.Equals("y") && vehNumbers.Count > 0)
                                    {
                                        currentFile = currentFile.AsParallel().Where(o => vehNumbers.Contains(o.Id)).ToList();
                                    }

                                    if (currentFile != null)
                                    {
                                        if (currentFile.Count > 0)
                                        {
                                            //currentFile = currentFile.AsParallel().OrderBy(o => o.GPStime).ToList();
                                            //unsavedFiles.TryAdd(filepath, new List<Vehicle>(currentFile))
                                            unsavedFiles.TryAdd(filepath, currentFile);

                                            processed_count++;
                                            Console.Write("\r{1} of {0} loaded, {2} of {0} processed, {3} of {0} written...   ", total_count, dataload_count, processed_count, write_count);
                                            //Console.Write(string.Format("{0},", filename));//update user
                                        }
                                        else
                                        {
                                            //revise count
                                            total_count--;
                                            dataload_count--;
                                        }
                                    }
                                    else
                                    {
                                        //revise count
                                        total_count--;
                                        dataload_count--;
                                    }
                                    //currentFile.Clear();
                                });
                                //}
                            },  // close first Action
                            () =>
                            {
                                //===write to file===
                                StreamWriter csv_file = new StreamWriter(csv_filepath);
                                string record = "";
                                string header = "Id,RouteTag,DirTag,Lat,Lon,SecsSinceReport,Predictable,Heading,GPStime\n";
                                record = record + header;

                                //flush tracker
                                double flush_counter = 0;

                                while (processed_count < total_count || unsavedFiles.Count > 0)
                                {
                                    if (unsavedFiles.Count > 0)
                                    {
                                        string filepath = unsavedFiles.Keys.First();
                                        List<Vehicle> currentFile;
                                        bool isFileRetrieve = unsavedFiles.TryGetValue(filepath, out currentFile);

                                        ////foreach (Vehicle veh in currentFile)
                                        //Parallel.ForEach(currentFile, veh =>
                                        //{
                                        //    string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}\n", veh.Id, veh.RouteTag, veh.DirTag, veh.Lat, veh.Lon, veh.SecsSinceReport, veh.Predictable, veh.Heading, veh.GPStime);//"id,routeTag,dirTag,lat,lon,secsSinceReport,predictable,heading,GPStime";
                                        //});
                                        ////}

                                        string currentRecord = CsvSerializer.SerializeToCsv(currentFile);
                                        currentRecord = currentRecord.Remove(0, 72);
                                        record += currentRecord;

                                        flush_counter += record.Length;

                                        //Flush approximately every 10MB
                                        if (flush_counter > 10000000)
                                        {
                                            Console.Write("\r Writing files...   ");

                                            //ProcessWrite(csv_filepath, record);
                                            csv_file.Write(record);
                                            csv_file.Flush();
                                            flush_counter = 0;
                                            record = "";//clear

                                            //clear memory
                                            GC.Collect();
                                            GC.WaitForPendingFinalizers();
                                        }

                                        unsavedFiles.TryRemove(filepath, out currentFile);

                                        write_count++;
                                        Console.Write("\r{1} of {0} loaded, {2} of {0} processed, {3} of {0} written...   ", total_count, dataload_count, processed_count, write_count);

                                        //clear memory
                                        currentFile.Clear();
                                    }
                                    else
                                    {
                                        Thread.Sleep(1000);//wait a sec
                                    }
                                }
                                csv_file.Close();
                            } //close third Action,
                            ); //close parallel.invoke

                            Console.WriteLine("[!] Processing Successful, exit to main menu.");
                        }
                    }//end if filter is y - yes
                    else
                    {
                        Console.WriteLine("[!] Invalid input, exit to main menu.");
                    }//end else invalid
                }//end if main request is w - write
            }
        }

        //#region ASYNC METHODS

        ////source-https://blogs.msdn.microsoft.com/csharpfaq/2012/01/23/using-async-for-file-access/
        //static Task ProcessWrite(string filePath, string text)
        //{
        //    return WriteTextAsync(filePath, text);
        //}

        //static async Task WriteTextAsync(string filePath, string text)
        //{
        //    byte[] encodedText = Encoding.Unicode.GetBytes(text);

        //    using (FileStream sourceStream = new FileStream(filePath,
        //        FileMode.Append, FileAccess.Write, FileShare.None,
        //        bufferSize: 4096, useAsync: true))
        //    {
        //        await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
        //        await sourceStream.FlushAsync();
        //    };
        //}

        //#endregion

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
                System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(serializableObject.GetType());
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

                    System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(outType);
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
}
