using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;

namespace RTP_QoS_tester
{
    /// <summary>
    /// Connects to an RTP stream and listens for data.
    /// </summary>
    public class RTPClient
    {
        private static readonly object Lock = new object();
        private static readonly object SessionLock = new object();
        private static Int32 _statFixer;
        private readonly List<Int32> _receivedPackets = new List<int>(2000);
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<bool, Action>> _taskQueue = new System.Collections.Concurrent.BlockingCollection<Tuple<bool, Action>>();
        private bool _setupComplete;
        private UdpClient _client;
        private IPEndPoint _endPoint;
        private Thread _listenerThread;
        private Thread _writeThread;

        System.Timers.Timer _timer;
        private readonly string _packetDumpFile = $"Packets_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.log";
        private readonly string _statsDumpFile = $"Statistics_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.txt";

        /// <summary>
        /// Gets whether the client is listening for packets
        /// </summary>
        public bool Listening { get; private set; }

        /// <summary>
        /// Gets an IPv4 address of broadcasting to measure.
        /// </summary>
        public IPAddress Address { get; }

        /// <summary>
        /// Gets the port the RTP client is listening on.
        /// </summary>
        public Int32 Port { get; }

        /// <summary>
        /// Gets an interval of packet statistics.
        /// </summary>
        public Int32 StatsInterval { get; private set; }

        /// <summary>
        /// Gets a switch if the statistics should be also dumped into the text file.
        /// </summary>
        public bool DumpStatsToFile { get; private set; }

        /// <summary>
        /// Gets a switch if the raw packet dump should be outputted also to console.
        /// <para>By default, the raw packet dump is store only in the log file.</para>
        /// </summary>
        public bool DumpPacketsToConsole { get; private set; }

        /// <summary>
        /// Gets a number of packets in entire session.
        /// </summary>
        public UInt64 NumberOfReceivedPacketInSession { get; private set; }

        /// <summary>
        /// Gets a number of packets in one UIn16 sequence.
        /// </summary>
        public UInt64 NumberOfReceivedPacketBySequence { get; private set; }

        /// <summary>
        /// Gets the first packet in entire session.
        /// </summary>
        public Int32 FirstPacketOfSession { get; private set; }

        /// <summary>
        /// Gets the last packet in entire session.
        /// </summary>
        public Int32 LastPacketOfSession { get; private set; }

        /// <summary>
        /// Gets the first packet time stamp in entire session.
        /// </summary>
        public Int32 FirstPacketOfSessionTimeStamp { get; private set; }

        /// <summary>
        /// Gets the first packet time stamp in the measured second.
        /// </summary>
        public Int32 FirstPacketOfSecondTimeStamp { get; set; }

        /// <summary>
        /// Gets the first packet in the measured second.
        /// </summary>
        public Int32 FirstPacketOfSecond { get; set; }

        /// <summary>
        /// Gets the number of captured packets in the measured second.
        /// </summary>
        public Int32 NumberOfReceivedPacketInSecond { get; set; }

        /// <summary>
        /// Gets the size of bytes in the measured second.
        /// </summary>
        public UInt64 SizeOfBytesInSecond { get; set; }

        /// <summary>
        /// RTP Client for receiving an RTP stream containing a WAVE audio stream
        /// </summary>
        /// <param name="address">An IPv4 address of broadcasting to measure.</param>
        /// <param name="port">The port to listen on</param>
        public RTPClient(IPAddress address, Int32 port)
        {
            Console.WriteLine("[RTPClient] Loading...");

            Address = address;
            Port = port;

            Console.WriteLine("[RTPClient] Done");
        }


        /// <summary>
        /// Creates a connection to the RTP stream
        /// </summary>
        public void StartClient()
        {
            if (!_setupComplete)
            {
                SetupClient();
            }
            Console.WriteLine($"[RTPClient] Listening for packets at {Address} on port {Port}...");
            NumberOfReceivedPacketInSession = 0;
            Listening = true;
            _timer.Enabled = true;
        }

        /// <summary>
        /// Setups the RTP client and all its components to be able to measure stream.
        /// </summary>
        /// <param name="statsInterval">An interval of statistics reporting.</param>
        /// <param name="dumpStatsToFile">A switch by which the statistics are also written into the dump file.</param>
        /// <param name="dumpPacketsToConsole">A switch by which the packet dump is also written into the console output.</param>
        public void SetupClient(Int32 statsInterval = 1000, bool dumpStatsToFile = true, bool dumpPacketsToConsole = true)
        {
            if (_setupComplete)
            {
                return;
            }
            // Create new UDP client. The IP end poInt32 tells us which IP is sending the data
            try
            {
                StatsInterval = statsInterval;
                DumpPacketsToConsole = dumpPacketsToConsole;
                DumpStatsToFile = dumpStatsToFile;
                _timer = new System.Timers.Timer();
                _timer.Elapsed += OnTimedEvent;
                _timer.Interval = StatsInterval;

                _endPoint = new IPEndPoint(IPAddress.Any, Port);
                _client = new UdpClient { ExclusiveAddressUse = false };
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _client.ExclusiveAddressUse = false;
                _client.Client.Bind(_endPoint);
                IPAddress multicastAddress = IPAddress.Parse(Address.ToString());
                _client.JoinMulticastGroup(multicastAddress);
                _listenerThread = new Thread(ReceiveCallback);
                _writeThread = new Thread(() =>
                {
                    while (true)
                    {
                        var item = _taskQueue.Take();
                        if (!item.Item1)
                        {
                            break;
                        }

                        item.Item2();
                    }
                });
                _listenerThread.Start();
                _writeThread.Start();
                _setupComplete = true;
                Console.WriteLine("[RTPClient] Setup has been completed.");
            }
            catch (ThreadStateException)
            {
                Console.WriteLine("A thread for broadcast received threw an exception.");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("There isn't enough memory to start ");
            }
            catch (SocketException e)
            {
                Console.WriteLine(
                    "An exception occurred during setup of the client socket. The original exception message:\n" +
                    e);
            }
            catch (Exception e)
            {
                Console.WriteLine(
                    "An unexpected exception occurred during setup of the client socket. The original exception message:\n" +
                    e);
            }
        }

        /// <summary>
        /// Tells the UDP client to stop listening for packets.
        /// </summary>
        public void StopClient()
        {
            // Set the boolean to false to stop the asynchronous packet receiving
            Console.WriteLine($"[RTPClient] Stopped listening for packets at {Address} on port {Port}...");
            Listening = false;
            _timer.Enabled = false;
            _taskQueue.Add(new Tuple<bool, Action>(false, () => Console.WriteLine("[RTPClient] Write task killed.")));
        }


        /// <summary>
        /// Handles the receiving of UDP packets from the RTP stream
        /// </summary>
        private void ReceiveCallback()
        {
            // Begin looking for the next packet
            while (Listening)
            {
                // Receive packet
                byte[] packet = _client.Receive(ref _endPoint);

                // Decode the header of the packet
                Int32 version = GetRTPHeaderValue(packet, 0, 1);
                Int32 padding = GetRTPHeaderValue(packet, 2, 2);
                Int32 extension = GetRTPHeaderValue(packet, 3, 3);
                Int32 csrcCount = GetRTPHeaderValue(packet, 4, 7);
                Int32 marker = GetRTPHeaderValue(packet, 8, 8);
                Int32 payloadType = GetRTPHeaderValue(packet, 9, 15);
                Int32 sequenceNum = GetRTPHeaderValue(packet, 16, 31);
                Int32 timestamp = GetRTPHeaderValue(packet, 32, 63);
                Int32 ssrcId = GetRTPHeaderValue(packet, 64, 95);
                string message = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] " +
                                 $"Version: {version} " +
                                 $"Padding: {padding} " +
                                 $"Extension: {extension} " +
                                 $"CSR: {csrcCount} " +
                                 $"Marker: {marker} " +
                                 $"PayloadType: {payloadType} " +
                                 $"SequenceNumber:{sequenceNum} " +
                                 $"TimeStamp: {timestamp} " +
                                 $"SsrcId: {ssrcId}";

                // Write the packet dump info
                _taskQueue.Add(new Tuple<bool, Action>(true, () => File.AppendAllLines(_packetDumpFile, new[] { message })));
                if (DumpPacketsToConsole)
                {
                    Console.WriteLine(message);
                }

                lock (SessionLock)
                {

                    if (NumberOfReceivedPacketInSession == 0 || (sequenceNum < FirstPacketOfSession && timestamp < FirstPacketOfSessionTimeStamp))
                    {
                        FirstPacketOfSession = sequenceNum;
                        FirstPacketOfSessionTimeStamp = timestamp;
                    }
                    if (sequenceNum < FirstPacketOfSession && timestamp > FirstPacketOfSessionTimeStamp)
                    {
                        NumberOfReceivedPacketBySequence += (UInt64)(LastPacketOfSession - FirstPacketOfSession + 1);
                        FirstPacketOfSession = sequenceNum;
                        FirstPacketOfSessionTimeStamp = timestamp;
                    }

                    NumberOfReceivedPacketInSession++;
                    LastPacketOfSession = sequenceNum;
                }

                lock (Lock)
                {
                    if (NumberOfReceivedPacketInSecond == 0)
                    {
                        FirstPacketOfSecond = sequenceNum;
                        FirstPacketOfSecondTimeStamp = timestamp;
                    }
                    if (sequenceNum < FirstPacketOfSecond && timestamp > FirstPacketOfSecondTimeStamp)
                    {
                        _receivedPackets.Add(UInt16.MaxValue + ++_statFixer);
                    }
                    else
                    {
                        _receivedPackets.Add(sequenceNum);
                    }
                    SizeOfBytesInSecond += (UInt64)packet.Length;
                    NumberOfReceivedPacketInSecond++;
                }

            }
        }

        // Specify what you want to happen when the Elapsed event is raised.
        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            if (!Listening || NumberOfReceivedPacketInSession <= 0)
            {
                return;
            }

            UInt64 totalNumberOfReceivedPacketBySequence;
            UInt64 numberOfReceivedPacketInSession;
            lock (SessionLock)
            {
                totalNumberOfReceivedPacketBySequence = (UInt64)(LastPacketOfSession - FirstPacketOfSession + 1) + NumberOfReceivedPacketBySequence;
                numberOfReceivedPacketInSession = NumberOfReceivedPacketInSession;
            }
            string message = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] " +
                             $"Rcvd (session): {numberOfReceivedPacketInSession.ToString().PadLeft(5)} " +
                             $"BySQ: {totalNumberOfReceivedPacketBySequence.ToString().PadLeft(5)} " +
                             $"Lost: {(totalNumberOfReceivedPacketBySequence - numberOfReceivedPacketInSession).ToString().PadLeft(5)} (" +
                             $"{((1.0 - ((double)numberOfReceivedPacketInSession / totalNumberOfReceivedPacketBySequence)) * 100.0).ToString("0.000").PadLeft(6)} %) | ";

            Int32 numberOfReceivedPacketInSecond;
            Int32 range;
            UInt64 sizeOfBytesInSecond;
            lock (Lock)
            {
                numberOfReceivedPacketInSecond = NumberOfReceivedPacketInSecond;
                sizeOfBytesInSecond = SizeOfBytesInSecond;
                range = Math.Abs(_receivedPackets.Max() - _receivedPackets.Min() + 1);
                _statFixer = 0;
                NumberOfReceivedPacketInSecond = 0;
                SizeOfBytesInSecond = 0;
                _receivedPackets.Clear();
            }

            if (numberOfReceivedPacketInSecond > 0)
            {
                message +=
                    $"Rcvd (s): {numberOfReceivedPacketInSecond.ToString().PadLeft(4)} " +
                    $"BySQ: {range.ToString().PadLeft(5)} " +
                    $"Lost: {Math.Abs(range - numberOfReceivedPacketInSecond).ToString().PadLeft(4)} (" +
                    $"{((1.0 - (double)numberOfReceivedPacketInSecond / range) * 100.0).ToString("0.000").PadLeft(6)} %) | " +
                    $"Speed: {(double)sizeOfBytesInSecond / 1024 / 1024 / (StatsInterval / 1e3):0.000} MB/s (" +
                    $"{((double)sizeOfBytesInSecond * 8 / 1e6 / (StatsInterval / 1e3)).ToString("0.000").PadLeft(6)} Mbps)";
            }

            Console.WriteLine(message);
            if (DumpStatsToFile)
            {
                _taskQueue.Add(new Tuple<bool, Action>(true, () => File.AppendAllLines(_statsDumpFile, new[] { message })));
            }
        }

        /// <summary>
        /// Grabs a value from the RTP header in Big-Endian format
        /// </summary>
        /// <param name="packet">The RTP packet</param>
        /// <param name="startBit">Start bit of the data value</param>
        /// <param name="endBit">End bit of the data value</param>
        /// <returns>The value</returns>
        private Int32 GetRTPHeaderValue(byte[] packet, Int32 startBit, Int32 endBit)
        {
            Int32 result = 0;

            // Number of bits in value
            Int32 length = endBit - startBit + 1;

            // Values in RTP header are big endian, so need to do these conversions
            for (Int32 i = startBit; i <= endBit; i++)
            {
                Int32 byteIndex = i / 8;
                Int32 bitShift = 7 - (i % 8);
                result += ((packet[byteIndex] >> bitShift) & 1) * (int)Math.Pow(2, length - i + startBit - 1);
            }
            return result;
        }
    }
}