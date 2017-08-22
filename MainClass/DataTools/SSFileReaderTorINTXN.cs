using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace MainClass.DataTools
{
    class SSFileReaderTorINTXN
    {
        public string trafficVolFilePath { get; set; }
        public string trafficSignalsFilePath { get; set; }
        public string pedCrossoverFilePath { get; set; }
        public string flashingBeaconFilePath { get; set; }

        /// <summary>
        /// Initiate a file reader, providing at least a file directory and traffic signals file name.
        /// </summary>
        /// <param name="fileDirectory">required</param>
        /// <param name="trafficSignalsFileName">required</param>
        /// <param name="trafficVolumeFileName">optional</param>
        /// <param name="pedCrossoverFileName">optional</param>
        /// <param name="flashingBeaconFileName">optional</param>
        public SSFileReaderTorINTXN(string trafficSignalsPath, string trafficVolumePath = null, string pedCrossoverPath = null, string flashingBeaconPath = null)
        {
            trafficSignalsFilePath = trafficSignalsPath;
            trafficVolFilePath = trafficVolumePath;
            pedCrossoverFilePath = pedCrossoverPath;
            flashingBeaconFilePath = flashingBeaconPath;
        }

        public ConcurrentDictionary<int, SSINTNXSignalDataTable> getCombinedIntxnData()
        {
            ConcurrentDictionary<int, SSINTNXSignalDataTable> combinedTable = new ConcurrentDictionary<int, SSINTNXSignalDataTable>();
            //try
            //{
            //read all Intxn data sheets from xml 
            //traffic_signals.xml
            if (trafficSignalsFilePath != null)
            {
                Traffic_signals trafficSignalData = SSUtil.DeSerializeXMLObject<Traffic_signals>(trafficSignalsFilePath);
                foreach (RecordTrafficSignals records in trafficSignalData.Record)
                {
                    int pxID = Convert.ToInt32(records.Px);
                    SSINTNXSignalDataTable commonData = combinedTable.ContainsKey(pxID) ? combinedTable[pxID] : new SSINTNXSignalDataTable();
                    commonData.pxID = pxID;
                    commonData.mainStreet = records.Main;
                    commonData.midblockStreet = records.Mid_block;
                    commonData.sideStreet1 = records.Side1;
                    commonData.sideStreet2 = records.Side2;
                    commonData.Latitude = Convert.ToDouble(records.Lat);
                    commonData.Longitude = Convert.ToDouble(records.Long);
                    commonData.signal_system = records.Signal_system;
                    commonData.mode_of_control = records.Mode_of_control;
                    commonData.isNotFXT = records.Mode_of_control.Contains("FXT") ? false : true;
                    commonData.transit_preempt = records.Transit_preempt.Contains("1") ? true : false;
                    commonData.fire_preempt = records.Fire_preempt.Contains("1") ? true : false;
                    commonData.Rail_preempt = records.Rail_preempt.Contains("1") ? true : false;
                    int noOfApproaches = records.No_of_signalized_approaches.Length == 1 ? Convert.ToInt32(records.No_of_signalized_approaches) : 0;
                    commonData.vehVol = -1;//no volume default
                    commonData.pedVol = -1;//no volume default
                    commonData.no_of_signalized_approaches = noOfApproaches;
                    commonData.isSignalizedIntxn = true;
                    commonData.isPedCross = false;
                    commonData.isFlasingBeacons = false;
                    combinedTable.AddOrUpdate(pxID, commonData, (k, v) => v);
                }
            }
            //pedestrian_crossovers.xml
            if (pedCrossoverFilePath != null)
            {
                Pedestrian_crossovers pedCrossData = SSUtil.DeSerializeXMLObject<Pedestrian_crossovers>(pedCrossoverFilePath);
                foreach (RecordPedCross records in pedCrossData.Record)
                {
                    int pxID = Convert.ToInt32(records.Px);
                    SSINTNXSignalDataTable commonData;
                    if (combinedTable.ContainsKey(pxID))
                    {
                        commonData = combinedTable[pxID];
                    }
                    else
                    {
                        commonData = new SSINTNXSignalDataTable();
                        commonData.pxID = pxID;
                        commonData.mainStreet = records.Main;
                        commonData.midblockStreet = records.Midblock;
                        commonData.sideStreet1 = records.Side1;
                        commonData.sideStreet2 = records.Side2;
                        commonData.Latitude = Convert.ToDouble(records.Lat);
                        commonData.Longitude = Convert.ToDouble(records.Long);
                        commonData.no_of_signalized_approaches = 2;
                        commonData.vehVol = -1;//no volume default
                        commonData.pedVol = -1;//no volume default
                    }
                    commonData.isSignalizedIntxn = false;
                    commonData.isPedCross = true;
                    commonData.isFlasingBeacons = false;
                    combinedTable.AddOrUpdate(pxID, commonData, (k, v) => v);
                }
            }
            //flashing_beacons.xml
            if (flashingBeaconFilePath != null)
            {
                Flashing_beacons flashingBeaconData = SSUtil.DeSerializeXMLObject<Flashing_beacons>(flashingBeaconFilePath);
                foreach (RecordFlashingBeacons records in flashingBeaconData.Record)
                {
                    int pxID = Convert.ToInt32(records.Px);
                    SSINTNXSignalDataTable commonData;
                    if (combinedTable.ContainsKey(pxID))
                    {
                        commonData = combinedTable[pxID];
                    }
                    else
                    {
                        commonData = new SSINTNXSignalDataTable();
                        commonData.pxID = pxID;
                        commonData.mainStreet = records.Main;
                        commonData.midblockStreet = records.Midblock;
                        commonData.sideStreet1 = records.Side1;
                        commonData.sideStreet2 = records.Side2;
                        commonData.Latitude = Convert.ToDouble(records.Lat);
                        commonData.Longitude = Convert.ToDouble(records.Long);
                        commonData.no_of_signalized_approaches = 2;
                        commonData.vehVol = -1;//no volume default
                        commonData.pedVol = -1;//no volume default
                    }
                    commonData.isSignalizedIntxn = false;
                    commonData.isPedCross = false;
                    commonData.isFlasingBeacons = true;
                    combinedTable.AddOrUpdate(pxID, commonData, (k, v) => v);
                }
            }
            //traffic_vols.xml
            if (trafficVolFilePath != null)
            {
                Traffic_vols trafficVolData = SSUtil.DeSerializeXMLObject<Traffic_vols>(trafficVolFilePath);
                foreach (RecordTrafficVols records in trafficVolData.Record)
                {
                    int pxID = Convert.ToInt32(records.Px);
                    SSINTNXSignalDataTable commonData;
                    if (combinedTable.ContainsKey(pxID))
                    {
                        commonData = combinedTable[pxID];
                    }
                    else
                    {
                        commonData = new SSINTNXSignalDataTable();
                        commonData.pxID = pxID;
                        commonData.mainStreet = records.Main;
                        commonData.midblockStreet = records.Midblock;
                        commonData.sideStreet1 = records.Side1;
                        commonData.sideStreet2 = records.Side2;
                        commonData.Latitude = Convert.ToDouble(records.Lat);
                        commonData.Longitude = Convert.ToDouble(records.Long);

                        commonData.signal_system = "unknownFromVolFile";
                        commonData.mode_of_control = "unknownFromVolFile";
                        commonData.isNotFXT = false;
                        commonData.transit_preempt = false;
                        commonData.fire_preempt = false;
                        commonData.Rail_preempt = false;
                        int noOfApproaches = (commonData.midblockStreet.Length > 0) ? 2 : ((commonData.sideStreet2.Contains("PRIVATE ACCESS")) ? 3 : 4);//estimates
                        commonData.no_of_signalized_approaches = noOfApproaches;
                        commonData.isSignalizedIntxn = true;//assumes only signalized intersection has volume measurements
                        commonData.isPedCross = false;
                        commonData.isFlasingBeacons = false;
                    }
                    commonData.countDate = DateTime.ParseExact(records.CountDate, "M-d-yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    string vehVol = Regex.Replace(records.VehVol8Hr, @"\s+", "");
                    commonData.vehVol = Convert.ToInt32(vehVol);
                    string pedVol = Regex.Replace(records.PedVol8Hr, @"\s+", "");
                    commonData.pedVol = Convert.ToInt32(pedVol);
                    combinedTable.AddOrUpdate(pxID, commonData, (k, v) => v);
                }
            }
            //}
            //catch (Exception e)
            //{
            //    return null;
            //}
            return combinedTable;
        }
    }

    // traffic volume data source, Open Data Toronto: 
    // converted to xml type using http://www.convertcsv.com/csv-to-xml.htm
    /* traffic volumes */
    [XmlRoot(ElementName = "record")]
    public class RecordTrafficVols
    {
        [XmlElement(ElementName = "px")]
        public string Px { get; set; }
        [XmlElement(ElementName = "main")]
        public string Main { get; set; }
        [XmlElement(ElementName = "midblock")]
        public string Midblock { get; set; }
        [XmlElement(ElementName = "side1")]
        public string Side1 { get; set; }
        [XmlElement(ElementName = "side2")]
        public string Side2 { get; set; }
        [XmlElement(ElementName = "activationDate")]
        public string ActivationDate { get; set; }
        [XmlElement(ElementName = "lat")]
        public string Lat { get; set; }
        [XmlElement(ElementName = "long")]
        public string Long { get; set; }
        [XmlElement(ElementName = "countDate")]
        public string CountDate { get; set; }
        [XmlElement(ElementName = "vehVol8Hr")]
        public string VehVol8Hr { get; set; }
        [XmlElement(ElementName = "pedVol8Hr")]
        public string PedVol8Hr { get; set; }
    }

    [XmlRoot(ElementName = "traffic_vols")]
    public class Traffic_vols
    {
        [XmlElement(ElementName = "record")]
        public List<RecordTrafficVols> Record { get; set; }
    }

    // traffic signal data source, Open Data Toronto: http://www1.toronto.ca/wps/portal/contentonly?vgnextoid=965b868b5535b210VgnVCM1000003dd60f89RCRD
    /* traffic signals */
    [XmlRoot(ElementName = "record")]
    public class RecordTrafficSignals
    {
        [XmlElement(ElementName = "px")]
        public string Px { get; set; }
        [XmlElement(ElementName = "main")]
        public string Main { get; set; }
        [XmlElement(ElementName = "mid_block")]
        public string Mid_block { get; set; }
        [XmlElement(ElementName = "side1")]
        public string Side1 { get; set; }
        [XmlElement(ElementName = "side2")]
        public string Side2 { get; set; }
        [XmlElement(ElementName = "private_access")]
        public string Private_access { get; set; }
        [XmlElement(ElementName = "additional_info")]
        public string Additional_info { get; set; }
        [XmlElement(ElementName = "geo_id")]
        public string Geo_id { get; set; }
        [XmlElement(ElementName = "node_id")]
        public string Node_id { get; set; }
        [XmlElement(ElementName = "x")]
        public string X { get; set; }
        [XmlElement(ElementName = "y")]
        public string Y { get; set; }
        [XmlElement(ElementName = "lat")]
        public string Lat { get; set; }
        [XmlElement(ElementName = "long")]
        public string Long { get; set; }
        [XmlElement(ElementName = "activation_date")]
        public string Activation_date { get; set; }
        [XmlElement(ElementName = "signal_system")]
        public string Signal_system { get; set; }
        [XmlElement(ElementName = "non_system")]
        public string Non_system { get; set; }
        [XmlElement(ElementName = "mode_of_control")]
        public string Mode_of_control { get; set; }
        [XmlElement(ElementName = "ped_walk_speed")]
        public string Ped_walk_speed { get; set; }
        [XmlElement(ElementName = "aps")]
        public string Aps { get; set; }
        [XmlElement(ElementName = "transit_preempt")]
        public string Transit_preempt { get; set; }
        [XmlElement(ElementName = "fire_preempt")]
        public string Fire_preempt { get; set; }
        [XmlElement(ElementName = "rail_preempt")]
        public string Rail_preempt { get; set; }
        [XmlElement(ElementName = "no_of_signalized_approaches")]
        public string No_of_signalized_approaches { get; set; }
    }

    [XmlRoot(ElementName = "traffic_signals")]
    public class Traffic_signals
    {
        [XmlElement(ElementName = "record")]
        public List<RecordTrafficSignals> Record { get; set; }
    }

    /* pedestrian crossovers */
    [XmlRoot(ElementName = "record")]
    public class RecordPedCross
    {
        [XmlElement(ElementName = "px")]
        public string Px { get; set; }
        [XmlElement(ElementName = "main")]
        public string Main { get; set; }
        [XmlElement(ElementName = "midblock")]
        public string Midblock { get; set; }
        [XmlElement(ElementName = "side1")]
        public string Side1 { get; set; }
        [XmlElement(ElementName = "side2")]
        public string Side2 { get; set; }
        [XmlElement(ElementName = "private_access")]
        public string Private_access { get; set; }
        [XmlElement(ElementName = "additional_info")]
        public string Additional_info { get; set; }
        [XmlElement(ElementName = "geo_id")]
        public string Geo_id { get; set; }
        [XmlElement(ElementName = "node_id")]
        public string Node_id { get; set; }
        [XmlElement(ElementName = "x")]
        public string X { get; set; }
        [XmlElement(ElementName = "y")]
        public string Y { get; set; }
        [XmlElement(ElementName = "lat")]
        public string Lat { get; set; }
        [XmlElement(ElementName = "long")]
        public string Long { get; set; }
    }

    [XmlRoot(ElementName = "pedestrian_crossovers")]
    public class Pedestrian_crossovers
    {
        [XmlElement(ElementName = "record")]
        public List<RecordPedCross> Record { get; set; }
    }

    /* flashing beacons */
    [XmlRoot(ElementName = "record")]
    public class RecordFlashingBeacons
    {
        [XmlElement(ElementName = "px")]
        public string Px { get; set; }
        [XmlElement(ElementName = "main")]
        public string Main { get; set; }
        [XmlElement(ElementName = "midblock")]
        public string Midblock { get; set; }
        [XmlElement(ElementName = "side1")]
        public string Side1 { get; set; }
        [XmlElement(ElementName = "side2")]
        public string Side2 { get; set; }
        [XmlElement(ElementName = "private_access")]
        public string Private_access { get; set; }
        [XmlElement(ElementName = "additional_info")]
        public string Additional_info { get; set; }
        [XmlElement(ElementName = "geo_id")]
        public string Geo_id { get; set; }
        [XmlElement(ElementName = "node_id")]
        public string Node_id { get; set; }
        [XmlElement(ElementName = "x")]
        public string X { get; set; }
        [XmlElement(ElementName = "y")]
        public string Y { get; set; }
        [XmlElement(ElementName = "lat")]
        public string Lat { get; set; }
        [XmlElement(ElementName = "long")]
        public string Long { get; set; }
    }

    [XmlRoot(ElementName = "flashing_beacons")]
    public class Flashing_beacons
    {
        [XmlElement(ElementName = "record")]
        public List<RecordFlashingBeacons> Record { get; set; }
    }
}
