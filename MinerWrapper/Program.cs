/*  --- License ---
 * Copyright (c) 2018 Metal-Ice (GNU Public 3.0)
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Linq;
using System.Runtime.InteropServices;

namespace MinerWrapper
{
    class Program
    {

        enum DisplayType
        {
            Summary,
            Detail,
            Menu
        }

        private static List<int> PORT_NO = new List<int> { 4028 };
        private static List<string> SERVER_IP = new List<string> { "127.0.0.1" };
        private static List<string> RIG_NAME = new List<string> { "LocalHost" };
        private static int timeout = 1000; //ms
        private static readonly int SummaryHeight = 7;
        private static DisplayType DT = DisplayType.Menu;

        private static List<MyStats> RigStats = new List<MyStats>();

        private static bool foundError = false;
        private static bool UpdateScreenSize = false;

        const int MF_BYCOMMAND = 0x00000000;
        const int SC_MINIMIZE = 0xF020;
        const int SC_MAXIMIZE = 0xF030;
        const int SC_SIZE = 0xF000;

        [DllImport("user32.dll")]
        public static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        static void Main(string[] args)
        {
            try
            {
                Console.WindowHeight = 11;
                Console.WindowWidth = 70;
                Console.BufferWidth = Console.WindowWidth;

                DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MINIMIZE, MF_BYCOMMAND);
                DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MAXIMIZE, MF_BYCOMMAND);
                DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_SIZE, MF_BYCOMMAND);

                LoadConfig();
                Console.Title = "RIG Poller";
                Console.CancelKeyPress += (sender, e) =>
                {

                    Console.WriteLine("Exiting...");
                    Environment.Exit(0);
                };

                var taskKeys = new Task(ReadKeys, TaskCreationOptions.PreferFairness);

                Task.Factory.StartNew(() => {
                    ReadKeys();
                }, CancellationToken.None, TaskCreationOptions.None, PriorityScheduler.BelowNormal);

                Timer t = new Timer(TimerCallback, null, 0, 5000);

                var tasks = new[] { taskKeys };
                Task.WaitAll(tasks);

                GC.KeepAlive(t);
            }
            catch (Exception)
            {
                Console.WriteLine("Could not start properly.");
            }
        }

        private static void LoadConfig()
        {
            try
            {
                var configFile = LoadConfigDocument();
                PORT_NO.Clear();
                SERVER_IP.Clear();
                RIG_NAME.Clear();
                RigStats.Clear();
                XmlNode node = configFile.SelectSingleNode("//settings");
                XmlNodeList hosts = configFile.GetElementsByTagName("host");
                foreach (XmlNode i in hosts)
                {
                    XmlNode ip = i.SelectSingleNode("ip");
                    XmlNode port = i.SelectSingleNode("port");
                    XmlNode name = i.SelectSingleNode("name");
                    SERVER_IP.Add(ip.InnerText);
                    PORT_NO.Add(Convert.ToInt16(port.InnerText));
                    RIG_NAME.Add(name.InnerText);
                }
                XmlNode timeoutNode = configFile.SelectSingleNode("//timeout");
                XmlNode timeoutValue = timeoutNode.SelectSingleNode("value");
                timeout = Convert.ToInt16(timeoutValue.InnerText);
            }
            catch (Exception)
            {
                Console.WriteLine("Could not load config file.");
            }
        }

        private static XmlDocument LoadConfigDocument()
        {
            XmlDocument doc = null;
            try
            {
                doc = new XmlDocument();
                doc.Load(System.IO.Directory.GetCurrentDirectory() + "\\config.xml");
                return doc;
            }
            catch (System.IO.FileNotFoundException e)
            {
                throw new Exception("No configuration file found.", e);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TimerCallback(Object o)
        {
            try
            {
                Task.Factory.StartNew(() => {
                    ProcessFiles();
                }, CancellationToken.None, TaskCreationOptions.None, PriorityScheduler.BelowNormal);

                GC.Collect();
            }
            catch (Exception) { }
        }

        private static string Client(string textToSend, int rigNumber)
        {
            var client = new TcpClient();
            if (!client.ConnectAsync(SERVER_IP[rigNumber], PORT_NO[rigNumber]).Wait(timeout))
            {
                Console.WriteLine("------------------");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to connect to rig: " + SERVER_IP[rigNumber]);
                Console.ResetColor();
                return string.Empty;
            }

            NetworkStream nwStream = client.GetStream();
            byte[] bytesToSend = ASCIIEncoding.ASCII.GetBytes(textToSend);

            nwStream.Write(bytesToSend, 0, bytesToSend.Length);

            byte[] bytesToRead = new byte[client.ReceiveBufferSize];
            int bytesRead = nwStream.Read(bytesToRead, 0, client.ReceiveBufferSize);

            client.Close();
            return Encoding.ASCII.GetString(bytesToRead, 0, bytesRead);
        }

        private static void ProcessFiles()
        {
            int currentIteration = 0;
            switch (DT)
            {
                case DisplayType.Summary:
                    try
                    {

                        if (foundError)
                        {
                            Console.Clear();
                            foundError = false;
                        }
                        Console.Clear();
                        Console.CursorVisible = false;
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine("->Summary View <Running...>");

                        for (int j = 0; j < SERVER_IP.Count; j++)
                        {
                            currentIteration = j;
                            var GpuCountString = string.Empty;
                            try
                            {
                                GpuCountString = Client("gpucount", j);
                            }
                            catch (SocketException)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Unable to contact rig!");
                                Console.ResetColor();
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("RIG IP:" + SERVER_IP[j] + ", Port:" + PORT_NO[j] + ", Name:" + RIG_NAME[j]);
                                Console.WriteLine("Updated:" + DateTime.Now);
                                Console.WriteLine();
                                continue;
                            }
                            catch (Exception)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Unable to contact rig!");
                                Console.ResetColor();
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("RIG IP:" + SERVER_IP[j] + ", Port:" + PORT_NO[j] + ", Name:" + RIG_NAME[j]);
                                Console.WriteLine("Updated:" + DateTime.Now);
                                Console.WriteLine();
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(GpuCountString))
                            {
                                Console.WriteLine("No Response From API");
                                Console.WriteLine("------------------");
                                foundError = true;
                                continue;
                            }
                            var GpuCount = (string.IsNullOrWhiteSpace(GpuCountString) ? 0 : Convert.ToInt16(GpuCountString.Substring(GpuCountString.LastIndexOf("=") + 1, GpuCountString.LastIndexOf("|") - (GpuCountString.LastIndexOf("=") + 1))));
                            double Total = 0.00;
                            for (int i = 0; i < GpuCount; i++)
                            {
                                var GpuDetails = Client("gpu|" + i, j);

                                if (GpuDetails.Contains("KHS 5s"))
                                {
                                    var speed = GpuDetails.Substring(GpuDetails.IndexOf("KHS av=") + 7, GpuDetails.IndexOf(",KHS 5s") - (GpuDetails.IndexOf("KHS av=") + 7));
                                    Total += Convert.ToDouble(speed);
                                    speed = (Convert.ToDouble(speed) * 1000).ToString();
                                }
                                else if (GpuDetails.Contains("KHS 30s"))
                                {
                                    var speed = GpuDetails.Substring(GpuDetails.IndexOf("KHS av=") + 7, GpuDetails.IndexOf(",KHS 30s") - (GpuDetails.IndexOf("KHS av=") + 7));
                                    Total += Convert.ToDouble(speed);
                                    speed = (Convert.ToDouble(speed) * 1000).ToString();
                                }
                                else
                                {
                                    Console.Write("Could not determine miner stats update interval");
                                    foundError = true;
                                    continue;
                                }
                            }

                            Console.Write("Name:" + RIG_NAME[j] + " ");
                            double average = RigStats.Where(a => a.Index == j).Select(a => a.HashRate).DefaultIfEmpty(0).Average();

                            Console.ForegroundColor = Total / average * 100 < 90 ? ConsoleColor.Red : ConsoleColor.Green;
                            Console.Write("Total:" + Total.ToString().Substring(0, Total.ToString().IndexOf(".") + Total.ToString().Length >= 6 ? 5 : 3) + "KH/s ");
                            Console.ResetColor();
                            if (average > 0)
                            {
                                Console.Write(" Avg:" + average.ToString().Substring(0, average.ToString().IndexOf(".") + average.ToString().Length >= 6 ? 5 : 3) + "KH/s ");
                            }

                            var MinerDetail = Client("coin", j);
                            var Description = MinerDetail.Substring(MinerDetail.IndexOf("Description=") + 12, MinerDetail.IndexOf("|COIN") - (MinerDetail.IndexOf("Description=") + 12));
                            Console.Write(" Miner:" + Description);
                            Description = MinerDetail.Substring(MinerDetail.IndexOf("Hash Method=") + 12, MinerDetail.IndexOf(",Current Block Time") - (MinerDetail.IndexOf("Hash Method=") + 12));
                            Console.Write(", Algo:" + Description);
                            Console.WriteLine();

                            if ((RigStats.Count + 1) * SERVER_IP.Count > 60 * SERVER_IP.Count)
                                RigStats.RemoveAt(0);
                            RigStats.Add(new MyStats() { Index = j, HashRate = Total });
                        }
                        Console.WriteLine();
                        Console.WriteLine("Compatible with 'sgminer-gm' and 'TeamRedMiner-v0.3.8' onwards. Last updated 30/11/2018.");
                        Console.WriteLine();
                        Console.WriteLine("ETH Donation Address: 0x715cef27f25040091da96ed76b83a7d5323012c7");
                        Console.WriteLine("BTC Donation Address: 16EdZ2fb51yHFR9MRvPbAJZv6rayAyigwY");
                        Console.WriteLine();
                        Console.WriteLine("Press (m) to display this menu. Press (q) to quit the program.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message != "One or more errors occurred." && ex.InnerException.ToString() != "No such host is known")
                            foundError = true;
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                        }
                    }
                    finally
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.Write(new string(' ', Console.WindowWidth));
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine("->Detailed View");
                        if (UpdateScreenSize)
                        {
                            Console.WindowHeight = 1 * SERVER_IP.Count + 9;
                            Console.WindowWidth = 90;
                            Console.BufferWidth = Console.WindowWidth;

                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MINIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MAXIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_SIZE, MF_BYCOMMAND);
                        }
                    }
                    break;
                case DisplayType.Detail:
                    try
                    {

                        if (foundError)
                        {
                            Console.Clear();
                            foundError = false;
                        }
                        Console.CursorVisible = false;
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine("->Detailed View <Running...>");

                        ClearPointers(SERVER_IP.Count);

                        for (int j = 0; j < SERVER_IP.Count; j++)
                        {
                            currentIteration = j;
                            var GpuCountString = string.Empty;
                            try
                            {
                                GpuCountString = Client("gpucount", j);
                            }
                            catch (SocketException)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Unable to contact rig!");
                                Console.ResetColor();
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("RIG IP:" + SERVER_IP[j] + ", Port:" + PORT_NO[j] + ", Name:" + RIG_NAME[j]);
                                Console.WriteLine("Updated:" + DateTime.Now);
                                Console.WriteLine();
                                continue;
                            }
                            catch (Exception)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Unable to contact rig!");
                                Console.ResetColor();
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("n/a");
                                Console.WriteLine("RIG IP:" + SERVER_IP[j] + ", Port:" + PORT_NO[j] + ", Name:" + RIG_NAME[j]);
                                Console.WriteLine("Updated:" + DateTime.Now);
                                Console.WriteLine();
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(GpuCountString))
                            {
                                Console.WriteLine("No Response From API");
                                Console.WriteLine("------------------");
                                foundError = true;
                                continue;
                            }
                            var GpuDetails = "";
                            var GpuCount = (string.IsNullOrWhiteSpace(GpuCountString) ? 0 : Convert.ToInt16(GpuCountString.Substring(GpuCountString.LastIndexOf("=") + 1, GpuCountString.LastIndexOf("|") - (GpuCountString.LastIndexOf("=") + 1))));
                            double Total = 0.00;

                            var MinerVersion = Client("version", j);

                            for (int i = 0; i < GpuCount; i++)
                            {
                                GpuDetails = Client("gpu|" + i, j);
                                if (GpuDetails.Contains("KHS 5s"))
                                {
                                    var speed = GpuDetails.Substring(GpuDetails.IndexOf("KHS av=") + 7, GpuDetails.IndexOf(",KHS 5s") - (GpuDetails.IndexOf("KHS av=") + 7));
                                    Total += Convert.ToDouble(speed);
                                    speed = (Convert.ToDouble(speed) * 1000).ToString();

                                    if (speed.Contains("."))
                                        Console.Write("GPU" + i + ":" + speed.Substring(0, speed.IndexOf(".") + 2) + "H/s, ");
                                    else if (Convert.ToDouble(speed) > 0)
                                        Console.Write("GPU" + i + ":" + Convert.ToDouble(speed) / 1000 + "KH/s, ");
                                    else
                                        Console.Write("GPU" + i + ":" + speed.Substring(0, speed.IndexOf(".") + 2) + "H/s, ");
                                }
                                else if (GpuDetails.Contains("KHS 30s"))
                                {
                                    var speed = GpuDetails.Substring(GpuDetails.IndexOf("KHS av=") + 7, GpuDetails.IndexOf(",KHS 30s") - (GpuDetails.IndexOf("KHS av=") + 7));
                                    Total += Convert.ToDouble(speed);
                                    speed = (Convert.ToDouble(speed) * 1000).ToString();

                                    if (speed.Contains("."))
                                        Console.Write("GPU" + i + ":" + speed.Substring(0, speed.IndexOf(".") + 2) + "H/s, ");
                                    else if (Convert.ToDouble(speed) > 0)
                                        Console.Write("GPU" + i + ":" + Convert.ToDouble(speed) / 1000 + "KH/s, ");
                                    else
                                        Console.Write("GPU" + i + ":" + speed.Substring(0, speed.IndexOf(".") + 2) + "H/s, ");
                                }
                                else
                                {
                                    Console.Write("Could not determine miner stats update interval");
                                    return;
                                }
                                GpuDetails = Client("gpu|" + i, j);
                                var Accepted = GpuDetails.Substring(GpuDetails.IndexOf("Accepted=") + 9, GpuDetails.IndexOf(",Rejected") - (GpuDetails.IndexOf("Accepted=") + 9));
                                Console.Write("A:" + Accepted + ", ");
                                var Rejected = GpuDetails.Substring(GpuDetails.IndexOf("Rejected=") + 9, GpuDetails.IndexOf(",Hardware Errors") - (GpuDetails.IndexOf("Rejected=") + 9));
                                Console.Write("R:" + Rejected + ((i + 1 != GpuCount) ? ", " : ", "));
                                var HWErrors = GpuDetails.Substring(GpuDetails.IndexOf("Hardware Errors=") + 16, GpuDetails.IndexOf(",Utility") - (GpuDetails.IndexOf("Hardware Errors=") + 16));
                                Console.Write("HW:" + HWErrors + ((i + 1 != GpuCount) ? "; " : ", "));
                                if (MinerVersion.Contains("sgminer") || MinerVersion.Contains("TeamRedMiner"))
                                {
                                    var temperature = GpuDetails.Substring(GpuDetails.IndexOf("Temperature=") + 12, GpuDetails.IndexOf(",Fan") - (GpuDetails.IndexOf("Temperature=") + 12));
                                    var FanSpeed = GpuDetails.Substring(GpuDetails.IndexOf("Fan Percent=") + 12, GpuDetails.IndexOf(",GPU Clock") - (GpuDetails.IndexOf("Fan Percent=") + 12));
                                    var GPUClocks = GpuDetails.Substring(GpuDetails.IndexOf("GPU Clock=") + 10, GpuDetails.IndexOf("Activity") - 25 - GpuDetails.IndexOf("GPU Clock=") + 10);
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.Write("Temp:" + temperature + "C, Fan%:" + FanSpeed + ", GPU Clock=" + GPUClocks + ", ");
                                    Console.ResetColor();
                                }
                                else
                                {
                                    Console.Write("Miner version unknown, will not track GPU information");
                                }

                                Console.ForegroundColor = ConsoleColor.Cyan;
                                var enabled = GpuDetails.Substring(GpuDetails.IndexOf("Enabled=") + 8, GpuDetails.IndexOf(",Status") - (GpuDetails.IndexOf("Enabled=") + 8));
                                Console.Write("Enabled:" + enabled + ", ");

                                var status = GpuDetails.Substring(GpuDetails.IndexOf("Status=") + 7, GpuDetails.IndexOf(",Temperature") - (GpuDetails.IndexOf("Status=") + 7));
                                if (status.Contains("Alive"))
                                {
                                    Console.Write("Status:" + status + " ");
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkRed;
                                    Console.Write("Status:" + status + " ");
                                    Console.ResetColor();
                                }
                                Console.WriteLine();
                                Console.ResetColor();
                            }

                            double average = RigStats.Where(a => a.Index == j).Select(a => a.HashRate).DefaultIfEmpty(0).Average();

                            Console.ForegroundColor = Total / average * 100 < 90 ? ConsoleColor.Red : ConsoleColor.Green;
                            Console.Write("Total:" + Total.ToString().Substring(0, Total.ToString().IndexOf(".") + Total.ToString().Length >= 6 ? 5 : 3) + "KH/s ");
                            Console.ResetColor();

                            Console.WriteLine();
                            var MinerDetail = Client("coin", j);
                            var Description = MinerDetail.Substring(MinerDetail.IndexOf("Description=") + 12, MinerDetail.IndexOf("|COIN") - (MinerDetail.IndexOf("Description=") + 12));
                            Console.Write("Miner:" + Description);
                            Description = MinerDetail.Substring(MinerDetail.IndexOf("Hash Method=") + 12, MinerDetail.IndexOf(",Current Block Time") - (MinerDetail.IndexOf("Hash Method=") + 12));
                            Console.Write(", Algo:" + Description);
                            Console.WriteLine();
                            Console.WriteLine("RIG IP:" + SERVER_IP[j] + ", Port:" + PORT_NO[j] + ", Name:" + RIG_NAME[j]);
                            Console.WriteLine("Updated:" + DateTime.Now);
                            Console.WriteLine("------------------");



                            if ((RigStats.Count + 1) * SERVER_IP.Count > 60 * SERVER_IP.Count)
                                RigStats.RemoveAt(0);
                            RigStats.Add(new MyStats() { Index = j, HashRate = Total });
                        }
                        Console.WriteLine();
                        Console.WriteLine("Compatible with 'sgminer-gm' and 'TeamRedMiner-v0.3.8' onwards. Last updated 30/11/2018.");
                        Console.WriteLine();
                        Console.WriteLine("ETH Donation Address: 0x715cef27f25040091da96ed76b83a7d5323012c7");
                        Console.WriteLine("BTC Donation Address: 16EdZ2fb51yHFR9MRvPbAJZv6rayAyigwY");
                        Console.WriteLine();
                        Console.WriteLine("Press (m) to display this menu. Press (q) to quit the program.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message != "One or more errors occurred." && ex.InnerException.ToString() != "No such host is known")
                            foundError = true;
                        else
                        {
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                            Console.WriteLine();
                        }
                    }
                    finally
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.Write(new string(' ', Console.WindowWidth));
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine("->Detailed View");
                        if (UpdateScreenSize)
                        {
                            Console.WindowHeight = SummaryHeight * SERVER_IP.Count + 11;
                            Console.WindowWidth = 140;
                            Console.BufferWidth = Console.WindowWidth;

                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MINIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MAXIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_SIZE, MF_BYCOMMAND);
                        }
                    }
                    break;
                case DisplayType.Menu:
                    try
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine("->Main Menu");
                        Console.WriteLine("===========");
                        Console.WriteLine("Press (1) to display a summary view of all monitored rigs.");
                        Console.WriteLine("Press (2) to display a detailed view of all monitored rigs.");
                        Console.WriteLine("Press (m) to display this menu.");
                        Console.WriteLine("Press (q) to quit the program.");
                        Console.WriteLine();
                        Console.WriteLine("ETH Donation Address: 0x715cef27f25040091da96ed76b83a7d5323012c7");
                        Console.WriteLine("BTC Donation Address: 16EdZ2fb51yHFR9MRvPbAJZv6rayAyigwY");
                        Console.WriteLine();

                        if (UpdateScreenSize)
                        {
                            Console.WindowHeight = 11;
                            Console.WindowWidth = 70;
                            Console.BufferWidth = Console.WindowWidth;

                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MINIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_MAXIMIZE, MF_BYCOMMAND);
                            DeleteMenu(GetSystemMenu(GetConsoleWindow(), false), SC_SIZE, MF_BYCOMMAND);
                        }
                    }
                    catch (Exception) { }
                    break;
                default:
                    break;
            }

        }

        private static void ClearPointers(int count)
        {
            try
            {
                int start = 2;
                for (int i = 0; i < count; i++)
                {
                    Console.SetCursorPosition(0, i * SummaryHeight + start);
                    Console.Write(new string(' ', Console.WindowWidth));

                    Console.SetCursorPosition(0, i * SummaryHeight + start + 1);
                    Console.Write(new string(' ', Console.WindowWidth));

                    Console.SetCursorPosition(0, i * SummaryHeight + start + 5);
                    Console.Write(new string(' ', Console.WindowWidth));
                }
                Console.SetCursorPosition(0, start);
            }
            catch (Exception) { }
        }

        private static void ReadKeys()
        {
            try
            {
                ConsoleKeyInfo key = new ConsoleKeyInfo();
                while (!Console.KeyAvailable && key.Key != ConsoleKey.Escape)
                {
                    key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        //    case ConsoleKey.R:
                        //        Client("gpurestart|N (0)", 0);
                        //        Console.WriteLine("GPU Restarted");
                        //        break;
                        //    case ConsoleKey.E:
                        //        Client("gpuenable|N (0)", 0);
                        //        Console.WriteLine("GPU Enabled");
                        //        break;
                        //    case ConsoleKey.D:
                        //        Client("gpudisable|N (0)", 0);
                        //        Console.WriteLine("GPU Disabled");
                        //        break;
                        //   case ConsoleKey.LeftArrow:
                        //       Console.WriteLine("LeftArrow was pressed");
                        //      break;
                        case ConsoleKey.Escape:
                            break;
                        case ConsoleKey.Q:
                            Environment.Exit(0);
                            break;
                        case ConsoleKey.D1:
                            DT = DisplayType.Summary;
                            Console.Clear();
                            UpdateScreenSize = true;
                            break;
                        case ConsoleKey.D2:
                            DT = DisplayType.Detail;
                            Console.Clear();
                            UpdateScreenSize = true;
                            break;
                        case ConsoleKey.M:
                            DT = DisplayType.Menu;
                            Console.Clear();
                            UpdateScreenSize = true;
                            break;
                        default:
                            if (Console.CapsLock && Console.NumberLock)
                            {
                                Console.WriteLine(key.KeyChar);
                            }
                            break;
                    }
                }
            }
            catch (Exception) { }
        }
    }
}

class MyStats
{
    public int Index { get; set; }
    public double HashRate { get; set; }
}

public class PriorityScheduler : TaskScheduler
{
    public static PriorityScheduler AboveNormal = new PriorityScheduler(ThreadPriority.AboveNormal);
    public static PriorityScheduler BelowNormal = new PriorityScheduler(ThreadPriority.BelowNormal);
    public static PriorityScheduler Lowest = new PriorityScheduler(ThreadPriority.Lowest);

    private BlockingCollection<Task> _tasks = new BlockingCollection<Task>();
    private Thread[] _threads;
    private readonly ThreadPriority _priority;
    private readonly int _maximumConcurrencyLevel = Math.Max(1, Environment.ProcessorCount);

    public PriorityScheduler(ThreadPriority priority)
    {
        _priority = priority;
    }

    public override int MaximumConcurrencyLevel
    {
        get { return _maximumConcurrencyLevel; }
    }

    protected override IEnumerable<Task> GetScheduledTasks()
    {
        return _tasks;
    }

    protected override void QueueTask(Task task)
    {
        _tasks.Add(task);

        if (_threads == null)
        {
            _threads = new Thread[_maximumConcurrencyLevel];
            for (int i = 0; i < _threads.Length; i++)
            {
                int local = i;
                _threads[i] = new Thread(() =>
                {
                    foreach (Task t in _tasks.GetConsumingEnumerable())
                        base.TryExecuteTask(t);
                })
                {
                    Name = string.Format("PriorityScheduler: ", i),
                    Priority = _priority,
                    IsBackground = true
                };
                _threads[i].Start();
            }
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false; // we might not want to execute task that should schedule as high or low priority inline
    }
}
