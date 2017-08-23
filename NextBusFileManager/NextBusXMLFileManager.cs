using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using CsvHelper;

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
                    List<Vehicle> finalFile = new List<Vehicle>();
                    Console.WriteLine("=> Enter output filename (no extension):");
                    string newfilename = Console.ReadLine();
                    Console.WriteLine("=> Do you want to specify filter? y - specify filter, n - no filter, save now.");
                    string filter_request = Console.ReadLine();
                    List<String> routeNumbers = new List<string>();
                    List<String> vehNumbers = new List<string>();
                    if (filter_request.Equals("y"))
                    {
                        string filter_option = "route";
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
                        if (filter_option.Equals("w"))
                        {
                            //go through every file and perform filter on the file, load filtered data
                            Console.WriteLine("=> Filtering all xml files in directory... Files processed: ");
                            string[] currentfilepaths = Directory.GetFiles(directory);//the filenames contain directory path
                            foreach (string filepath in currentfilepaths)
                            {
                                List<Vehicle> currentFile = new List<Vehicle>();
                                string filename = filepath.Split('\\').Last();
                                if (File.Exists(filepath) && filepath.Contains(".xml"))
                                {
                                    //load this file
                                    currentFile.AddRange(DeSerializeXMLObject<List<Vehicle>>(filepath));
                                    //process and filter this file
                                    if (routeNumbers.Count > 0)
                                    {
                                        //contains exacly the route number
                                        currentFile = currentFile.Where(o => routeNumbers.Contains(o.RouteTag)).ToList();

                                        ////contains any part of the route number
                                        //List<Vehicle> intermediateFile = new List<Vehicle>();
                                        //foreach (string number in routeNumbers)
                                        //{
                                        //    intermediateFile.AddRange(currentFile.Where(o => o.RouteTag.Contains(number)).ToList());
                                        //}
                                        //currentFile = intermediateFile;
                                    }
                                    if (vehNumbers.Count > 0)
                                    {
                                        currentFile = currentFile.Where(o => vehNumbers.Contains(o.Id)).ToList();
                                    }
                                    currentFile = currentFile.OrderBy(o => o.GPStime).ToList();
                                    finalFile.AddRange(new List<Vehicle>(currentFile));//add a copy

                                    Console.Write(string.Format("{0},", filename));//update user
                                }
                                else
                                {
                                    Console.WriteLine(string.Format("[!] File \"{0}\" cannot be loaded.", filename));//update user
                                }

                                //clear memory
                                currentFile.Clear();
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                            Console.WriteLine("=> All xml files processed, writting files...");

                            //write to xml
                            SerializeXMLObject(finalFile, directory + newfilename + ".xml");
                            //write to csv
                            using (var csv = new CsvWriter(new StreamWriter(directory + newfilename + ".csv")))
                            {
                                csv.WriteRecords(finalFile);
                            }
                            filter_request = "n";//exit cond
                            Console.WriteLine("[!] Write Successful, exit to main menu.");
                        }
                    }//end if filter is y - yes
                    else if (filter_request.Equals("n"))
                    {
                        Console.WriteLine("=> Adding all xml files in directory... Files added: ");
                        string[] currentfilepaths = Directory.GetFiles(directory);//the filenames contain directory path
                        foreach (string filepath in currentfilepaths)
                        {
                            List<Vehicle> currentFile = new List<Vehicle>();
                            string filename = filepath.Split('\\').Last();
                            if (File.Exists(filepath) && filepath.Contains(".xml"))
                            {
                                //load this file
                                currentFile.AddRange(DeSerializeXMLObject<List<Vehicle>>(filepath));
                                currentFile = currentFile.OrderBy(o => o.GPStime).ToList();
                                finalFile.AddRange(new List<Vehicle>(currentFile));//add a copy

                                Console.Write(string.Format("{0},", filename));//update user
                            }
                            else
                            {
                                Console.WriteLine(string.Format("[!] File \"{0}\" cannot be loaded.", filename));//update user
                            }

                            //clear memory
                            currentFile.Clear();
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        Console.WriteLine("=> All xml files added, writting files...");

                        //write to xml
                        SerializeXMLObject(finalFile, directory + newfilename + ".xml");
                        //write to csv
                        using (var csv = new CsvWriter(new StreamWriter(directory + newfilename + ".csv")))
                        {
                            csv.WriteRecords(finalFile);
                        }
                        filter_request = "n";//exit cond
                        Console.WriteLine("[!] Write Successful, exit to main menu.");
                    }//end else if n - no
                    else
                    {
                        Console.WriteLine("[!] Invalid input, exit to main menu.");
                    }//end else invalid
                }//end if main request is w - write
            }
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
