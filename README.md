# Transit Data Tool
Data collection tools for transit data.

## Features
- Downloaders for Toronto data
	- NextBus AVL Data
	- Open data Toronto road restrictions
	- OpenWeatherMap data for Toronto region (developers should update API key)

## Installation from release
- Download release zip, then unzip on your local drive.
- Go to 'TransitDataTool\AppData' folder, this is where all of the data will be downloaded to.
- Modify 'DataDownload-Setting.txt' inside the 'TransitDataTool\AppData' folder. This file indicates the start and end date & time of the data you want to download.
- Run
	- Main GUI: 'TransitDataTool\GUIHost.exe' -> Start Download using this utility program.
	- NextBus file manager: 'TransitDataTool_NextBusFileManager\NextBusFileManager.exe' -> Manage downloaded NextBus XML data.
	
## Installation from source code
### Application Data
- Unzip AppData.zip content to the directory of your compiled program (where GUIHost.exe is compiled).
- Modify the start and end date & time in 'DataDownload-Setting.txt'
### Dependencies
- Install NuGet Package Manager for MSVC
- Restore NuGet Packages, or install the packages listed in Requirements.md, via the NuGet console
### API keys
- Please set up your own API keys for OpenWeatherMap data and change the OpenWeather API Key before compiling
	- 'https://home.openweathermap.org/users/sign_up'
	
## Acknowledgements and Licensing
- This program is open-source under the MIT license term.

## Developments and Bugs
- Please report any bugs by posting a new issue.
- Participate in our software development efforts! You are welcome to fork and submit pull requests. If you want to be more involved, such as developing versions of this program for other transit agencies with additional transit-related datasets. Pitch your ideas in the 'Future Developments' issue.
