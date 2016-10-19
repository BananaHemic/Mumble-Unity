# Mumble-Unity

A simple to use Mumble Client made for Unity3D.
Easily send low-latency text and voice to a group of other users

## Features
* End-to-End OCB/AES encryption
* Client-to-server architecture allowing far more scalability than peer-to-peer
* Opus codec support, allowing lightweight low latency HD voice communication
* Open source server
* Works with other Mumble clients out of the box, provided they use the opus codec
* In game positional audio

## Limitations
* Only the Opus codec is supported, meaning very old mumble clients may be unable to connect
* Requires a Mumble server

## Getting started
### Get a server
..Either get a Mumble server at [one if the many Mumble server hosts](https://wiki.mumble.info/wiki/Hosters).
Or you can [setup your own Mumble server](https://wiki.mumble.info/wiki/Installing_Mumble) (which I recommend)

..If you do make your own server, be sure to set "opusthreshold=0" in mumble.ini in order to make all clients use opus

## TODO
1. Android support
2. Refractor
3. Optimize GC usage
