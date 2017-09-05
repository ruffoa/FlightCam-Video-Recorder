# FlightCam-Video-Recorder
Records multiple streams from Axis IP cameras and merges them into a single video file along with real time flight data.

Currently this app supports recording from up to 3 Axis cameras, and grabbing real time data from a Ublox GPS reciever and a ADXL345 accelerometer.  The resulting video file is stored as either a .alx file or a standardized .mkv with up to 4 video streams contained within the chosen container.  As a result of the slightly unconventional file type, most players will not play the recorded video properly, or will only play the primary video stream.  The one major exception to this rule is VLC, which supports displaying all of the embed video streams in the file.


_______________________________________________________________________________________________________________________________________
Thanks to the FFMPEG team, Zeranoe's Windows FFMPEG builds and Jacobbo's WebEye streamplayer (https://github.com/jacobbo/WebEye)
