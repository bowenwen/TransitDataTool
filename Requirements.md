//NOTE: the best practice is to restore all the NuGet packages from MSVC Package Manager Console, use these commands as a last resort.
//Install NuGet Package Manager for MSVC
//Using NuGet Console, install the below packages
Install-Package EntityFramework
Install-Package log4net
Install-Package ModernUI.WPF
Install-Package Microsoft.Tpl.Dataflow
Install-Package Newtonsoft.Json
Install-Package protobuf-net
//Install-Package XAML.MapControl
//Install-Package Microsoft.R.Host.Client.API
//Install-Package R.NET
Install-Package R.NET.Community -Version 1.6.5
Install-Package Selenium.WebDriver
Install-Package System.Data.SQLite
//Install-Package GMap.NET.Presentation
Install-Package GMap.NET.Windows
Install-Package CsvHelper
Install-Package EPPlus -Version 4.1.0
Install-Package MathNet.Numerics -Version 3.19.0
//for GMap.Net, make sure you add it to project reference: http://www.independent-software.com/gmap-net-beginners-tutorial-maps-markers-polygons-routes-updated-for-visual-studio-2015-and-gmap-net-1-7/
//for GMap.Net, if you encounter unable to load due to SQLite.Interop.dll error, copy all of SQLite dlls to the where Gmap's dlls are.
//Follow installation guide to download and set up Application Data
//https://docs.microsoft.com/en-us/nuget/consume-packages/reinstalling-and-updating-packages