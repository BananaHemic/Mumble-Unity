# Mumble-Unity

A simple to use Mumble Client made for Unity3D.
Easily send low-latency text and voice to a group of other users

## Features
* End-to-End OCB/AES encryption
* Client-to-server architecture allowing far more scalability than peer-to-peer
* Opus codec support, allowing lightweight low latency HD voice communication
* Open source server
* Works with other Mumble clients out of the box, provided they use the opus codec

## Limitations
* Only the Opus codec is supported, meaning very old mumble clients may be unable to connect
* Requires a Mumble server

## TODO
1. Android support
2. Refractor
3. Optimize GC usage
