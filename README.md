# Transit Data Tool
Data collection tools for transit data.

## Features
- Downloaders for Toronto data
	- NextBus AVL Data
	- Open data Toronto road restrictions
	- OpenWeatherMap data for Toronto region (should update API key)

## Installation from release
- Download release zip, then unzip on your local drive.
- Run
	- Main GUI: 'TransitDataTool\GUIHost.exe'
	- NextBus file manager: 'TransitDataTool_NextBusFileManager\NextBusFileManager.exe'
	
## Installation from source code
### Application Data
- unzip AppData.zip content to the directory of your compiled program (where GUIHost.exe is compiled).
### Dependencies
- Install NuGet Package Manager for MSVC
- Restore NuGet Packages, or install the packages listed in Requirements.md, via the NuGet console
### API keys
- Please set up your own API keys for OpenWeatherMap data and change the OpenWeather API Key before compiling
	- 'https://home.openweathermap.org/users/sign_up'
	
## Acknowledgements and Licensing
- This program is open-source under the MIT license term.
