# Mumble-Unity

A simple to use Mumble Client made for Unity3D.
Easily send low-latency text and voice to a group of other users

## Features

* End-to-End OCB/AES encryption
* Client-to-server architecture allowing far more scalability than peer-to-peer
* Opus codec support, allowing lightweight low latency HD voice communication
* Open source server
* Extensible server, allowing you to create the permissions structure best suited for your application
* Works with other Mumble clients out of the box, provided that they use the opus codec
* Works with any opus-compatible Mumble SDK, including sdks for:
  * [C++](https://github.com/mumble-voip/mumble)
  * [Objective-C](https://github.com/mumble-voip/mumblekit)
  * [Android/Java](https://github.com/pcgod/mumble-android)
  * [Ruby](https://github.com/mattvperry/mumble-ruby)
  * [Python](https://github.com/frymaster/mumbleclient)
  * [C#](https://github.com/martindevans/MumbleSharp)
  * [NodeJS](https://github.com/Rantanen/node-mumble)
  * Probably some others that I've forgotten to mention
* Cross platform

## Limitations

* Only the Opus codec is supported, meaning very old mumble clients may be unable to connect
* Requires a (free) Mumble server

## Getting started

### Get a server

   Either get a Mumble server at [one of the many Mumble server hosts](https://wiki.mumble.info/wiki/Hosters).
Or you can [setup your own Mumble server](https://wiki.mumble.info/wiki/Installing_Mumble) (which I recommend)

   If you do make your own server, be sure to set "opusthreshold=0" in mumble-server.ini or murmur.ini in order to make all clients use opus

## Installing

### Manual Method
   * Download / clone this repo into your Assets folder
   * Download the [protobuf dll](https://github.com/BananaHemic/Mumble-Unity/blob/per-speaker-codec/protobuf-net.dll) and extract into your Assets folder
   * Then, simply open the included example scene and input your Mumble server's address into "MumbleTester"
     
### Package Method 
   * Open the Packages/manifest.json file
   * Add the following line under dependencies
      `"com.bananahemic.mumble": "1.0.0",
   * After the dependencies section add nuget support (as outlined [here](https://github.com/xoofx/UnityNuGet#add-scope-registry-manifestjson))
   ```
     "scopedRegistries": [
    {
      "name": "Unity NuGet",
      "url": "https://unitynuget-registry.azurewebsites.net",
      "scopes": [
        "org.nuget"
      ]
    },
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
          "com.bananahemic.mumble"
      ]
  }
  ]
  ```
* Then, in Unity, copy over the Example scene (Packages/Mumble/Samples/Example.unity) to your assets
* Open the scene and input your Mumble server's address into "MumbleTester"

### Integration

   * To begin integrating into your application, you can copy "MumbleTester.cs" and adapt the functions to best meet your individual needs
   * For instance, if you would like to make the audio come from an object (like a person's head) you can change the `CreateMumbleAudioPlayerFromPrefab` method to create the prefab as a child of your target object

## TODO

1. Better support multiple audio per packet sizes (20ms is currently assumed)
2. Switch to TCP without sending voice packets
3. Get Opus libraries for iOS
4. Get Opus libraries for Linux

If you have any questions or errors, please feel free to open an issue
As always, stars are more than welcome
