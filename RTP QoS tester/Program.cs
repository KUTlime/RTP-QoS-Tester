using System.Net;

namespace RTP_QoS_tester
{
    class Program
    {
        static void Main(string[] args)
        {
            var rtpClient = new RTPClient(IPAddress.Parse(Properties.Settings.Default.MulticastIP), Properties.Settings.Default.MulticastPort);
            rtpClient.SetupClient(Properties.Settings.Default.StatsInterval, Properties.Settings.Default.DumpStatsToFile, Properties.Settings.Default.DumpPacketsToConsole);
            rtpClient.StartClient();
        }
    }
}
