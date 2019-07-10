# RTP QoS Tester
> Real-time Transport Protocol Quality of Service Tester.

# Introduction
A console application for Quality of Service measurement. It provides a statistics how much packet were lost during transmission over network. The statistics are calculated based on the information provided in packet headers. You can use it when you don't what to bother with WireShark.


![Preview](https://github.com/KUTlime/RTP-QoS-Tester/raw/master/image/Statistics.png)

# Properties
### MulticastIP (string)
An IPv4 address where the stream is comming from

### Multicast port (Int32)
A listening port where the tester is listening to a stream.

### StatsInterval (Int32)
An interval are statistics collected.

### DumpStatsToFile (bool)
If true, the statistics are dumped to a log file located next to the `RTP QoS tester.exe` named as Stats_yyyy-MM-dd_HHmmss.log. By default, the statistics are dumped to the console window.

### DumpPacketsToConsole (bool)
If true, the packets are dumped to the console windows. By default, the packets are dumped to the text file located next to the `RTP QoS tester.exe` named as Packet_yyyy-MM-dd_HHmmss.txt for further analysis.

