# Mumble-Unity

A simple to use Mumble Client made for Unity3D.
Easily send low-latency text and voice to a group of other users

## Features
* End-to-End OCB/AES encryption
* Client-to-server architecture allowing far more scalability than peer-to-peer
* Opus codec support, allowing lightweight low latency HD voice communication
* Open source server
* Extensible server, allowing you to create the permissions structure best suited for your application
* Works with other Mumble clients out of the box, provided they use the opus codec
* Works with any opus-compatible Mumble SDK, including sdks for:
  * [C++](https://github.com/mumble-voip/mumble)
  * [Objective-C](https://github.com/mumble-voip/mumblekit)
  * [Android/Java](https://github.com/pcgod/mumble-android)
  * [Ruby](https://github.com/mattvperry/mumble-ruby)
  * [Python](https://github.com/frymaster/mumbleclient)
  * [C#](https://github.com/martindevans/MumbleSharp)
  * [NodeJS](https://github.com/Rantanen/node-mumble)
  * Probably some other that I've forgotten to mention
* In game positional audio
* Cross platform

## Limitations
* Only the Opus codec is supported, meaning very old mumble clients may be unable to connect
* Requires a Mumble server

## Getting started
### Get a server
   Either get a Mumble server at [one of the many Mumble server hosts](https://wiki.mumble.info/wiki/Hosters).
Or you can [setup your own Mumble server](https://wiki.mumble.info/wiki/Installing_Mumble) (which I recommend)

   If you do make your own server, be sure to set "opusthreshold=0" in mumble.ini in order to make all clients use opus

## TODO
1. Make decoding happen off the main thread
2. Minor GC optimizations
3. Get Opus libraries for Mac/Linux
