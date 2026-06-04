using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace _3DSViGEm
{
    class Program
    {
        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr vigem_alloc();

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern VIGEM_ERROR vigem_connect(IntPtr c);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void vigem_disconnect(IntPtr c);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void vigem_free(IntPtr c);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr vigem_target_ds4_alloc();

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern VIGEM_ERROR vigem_target_add(IntPtr c, IntPtr t);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern VIGEM_ERROR vigem_target_remove(IntPtr c, IntPtr t);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void vigem_target_free(IntPtr t);

        [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern VIGEM_ERROR vigem_target_ds4_update_ex(
            IntPtr c, IntPtr t,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] report,
            uint reportLength);

        enum VIGEM_ERROR : uint { VIGEM_ERROR_NONE = 0x20000000 }

        const uint KEY_A      = 1 << 0;
        const uint KEY_B      = 1 << 1;
        const uint KEY_SELECT = 1 << 2;
        const uint KEY_START  = 1 << 3;
        const uint KEY_DRIGHT = 1 << 4;
        const uint KEY_DLEFT  = 1 << 5;
        const uint KEY_DUP    = 1 << 6;
        const uint KEY_DDOWN  = 1 << 7;
        const uint KEY_R      = 1 << 8;
        const uint KEY_L      = 1 << 9;
        const uint KEY_X      = 1 << 10;
        const uint KEY_Y      = 1 << 11;
        const uint KEY_ZL     = 1 << 14;
        const uint KEY_ZR     = 1 << 15;

        const byte DS4_DPAD_NONE = 0x8;
        const byte DS4_DPAD_N    = 0x0;
        const byte DS4_DPAD_NE   = 0x1;
        const byte DS4_DPAD_E    = 0x2;
        const byte DS4_DPAD_SE   = 0x3;
        const byte DS4_DPAD_S    = 0x4;
        const byte DS4_DPAD_SW   = 0x5;
        const byte DS4_DPAD_W    = 0x6;
        const byte DS4_DPAD_NW   = 0x7;

        const int DEFAULT_PORT = 8888;
        const int PACKET_SIZE = 18;

        static double _gyroSensitivityDivider = 90.0; 
        static double _gyroDeadzone = 25.0; 

        static volatile bool _running = true;
        static string _localIp = "Unknown";
        static int _port = DEFAULT_PORT;
        static string _connectionStatus = "Disconnected";
        static GyroCalib _calib;
        static UdpClient? _udp;
        static volatile bool _isMenuInteracting = false;

        struct GyroCalib
        {
            public float BiasX, BiasY, BiasZ;   
            public float ScaleX, ScaleY, ScaleZ; 
            public bool IsCalibrated;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            
            _localIp = GetLocalIPAddress();

            IntPtr vc = vigem_alloc();
            if (vc == IntPtr.Zero) { Console.WriteLine("[ERR] vigem_alloc failed."); Pause(); return 1; }

            var err = vigem_connect(vc);
            if (err != VIGEM_ERROR.VIGEM_ERROR_NONE)
            { Console.WriteLine($"[ERR] vigem_connect: {err}"); vigem_free(vc); Pause(); return 1; }

            IntPtr pad = vigem_target_ds4_alloc();
            err = vigem_target_add(vc, pad);
            if (err != VIGEM_ERROR.VIGEM_ERROR_NONE)
            { Console.WriteLine($"[ERR] vigem_target_add: {err}"); vigem_disconnect(vc); vigem_free(vc); Pause(); return 1; }

            _calib = new GyroCalib { BiasX = 0, BiasY = 0, BiasZ = 0, ScaleX = 1.0f, ScaleY = 1.0f, ScaleZ = 1.0f, IsCalibrated = false };

            PrintBanner();
            Console.WriteLine($"  Your local IP: {_localIp}");
            Console.Write($"  Enter the port to connect to (default {DEFAULT_PORT}): ");
			string? portInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput, out int customPort) && customPort > 0 && customPort <= 65535)
                _port = customPort;

            try
            {
                _udp = new UdpClient(_port);
                _udp.Client.ReceiveTimeout = 1000; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] UDP port {_port}: {ex.Message}");
                vigem_target_remove(vc, pad); vigem_target_free(pad); vigem_disconnect(vc); vigem_free(vc); Pause(); return 1;
            }

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _running = false; _udp?.Close(); };

            byte[] report = new byte[63];
            ResetReportToDefault(report);
            try { vigem_target_ds4_update_ex(vc, pad, report, 63); } catch { }

            Thread inputThread = new Thread(ConsoleInputLoop);
            inputThread.IsBackground = true;
            inputThread.Start();

            int packets = 0;
            var remote = new IPEndPoint(IPAddress.Any, 0);
            DateTime lastPacketTime = DateTime.MinValue;

            UpdateConsoleUI();

            while (_running)
            {
                if (_isMenuInteracting)
                {
                    Thread.Sleep(10);
                    continue;
                }

                byte[]? data = null;
                try 
                { 
                    data = _udp.Receive(ref remote); 
                    lastPacketTime = DateTime.UtcNow;
                    
                    if (_connectionStatus == "Disconnected")
                    {
                        _connectionStatus = "Connected";
                        UpdateConsoleUI();
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        if (!_isMenuInteracting && _connectionStatus == "Connected" && (DateTime.UtcNow - lastPacketTime).TotalSeconds >= 3.0)
                        {
                            _connectionStatus = "Disconnected";
                            UpdateConsoleUI();
                            
                            ResetReportToDefault(report);
                            try { vigem_target_ds4_update_ex(vc, pad, report, 63); } catch { }
                        }
                        continue; 
                    }
                    if (!_running) break;
                }
                catch { break; }

                if (data == null || _isMenuInteracting) continue;

                if (data.Length == 5 && data[0] == 'p' && data[1] == 'i' && data[2] == 'n' && data[3] == 'g')
                {
                    try { _udp.Send(Encoding.ASCII.GetBytes("pong"), 4, remote); } catch { }
                    continue;
                }

                if (data.Length < PACKET_SIZE) continue;

                uint  buttons = BitConverter.ToUInt32(data,  0);
                short cpX     = BitConverter.ToInt16 (data,  4);
                short cpY     = BitConverter.ToInt16 (data,  6);
                short csX     = BitConverter.ToInt16 (data,  8);
                short csY     = BitConverter.ToInt16 (data, 10);
                short rawGX   = BitConverter.ToInt16 (data, 12);
                short rawGY   = BitConverter.ToInt16 (data, 14);
                short rawGZ   = BitConverter.ToInt16 (data, 16);

                double calX = (rawGX - _calib.BiasX);
                double calY = (rawGY - _calib.BiasY);
                double calZ = (rawGZ - _calib.BiasZ);

                double currentDz = _gyroDeadzone;
                if (Math.Abs(calX) < currentDz) calX = 0; else calX = calX > 0 ? calX - currentDz : calX + currentDz;
                if (Math.Abs(calY) < currentDz) calY = 0; else calY = calY > 0 ? calY - currentDz : calY + currentDz;
                if (Math.Abs(calZ) < currentDz) calZ = 0; else calZ = calZ > 0 ? calZ - currentDz : calZ + currentDz;

                double currentDiv = _gyroSensitivityDivider;
                short ds4GyroX = (short)Clamp((int)(-calX / currentDiv), -32768, 32767);
                short ds4GyroY = (short)Clamp((int)(-calY / currentDiv), -32768, 32767);
                short ds4GyroZ = (short)Clamp((int)(calZ / currentDiv), -32768, 32767);

                bool up    = (buttons & KEY_DUP)    != 0;
                bool down  = (buttons & KEY_DDOWN)  != 0;
                bool left  = (buttons & KEY_DLEFT)  != 0;
                bool right = (buttons & KEY_DRIGHT) != 0;

                byte dpad = DS4_DPAD_NONE;
                if      (up   && right) dpad = DS4_DPAD_NE;
                else if (down && right) dpad = DS4_DPAD_SE;
                else if (down && left)  dpad = DS4_DPAD_SW;
                else if (up   && left)  dpad = DS4_DPAD_NW;
                else if (up)            dpad = DS4_DPAD_N;
                else if (right)         dpad = DS4_DPAD_E;
                else if (down)          dpad = DS4_DPAD_S;
                else if (left)          dpad = DS4_DPAD_W;

                byte b4 = dpad;
                if ((buttons & KEY_X) != 0) b4 |= 0x80; 
                if ((buttons & KEY_A) != 0) b4 |= 0x40; 
                if ((buttons & KEY_B) != 0) b4 |= 0x20; 
                if ((buttons & KEY_Y) != 0) b4 |= 0x10; 

                byte b5 = 0;
                if ((buttons & KEY_L)      != 0) b5 |= 0x01;
                if ((buttons & KEY_R)      != 0) b5 |= 0x02;
                if ((buttons & KEY_ZL)     != 0) b5 |= 0x04;
                if ((buttons & KEY_ZR)     != 0) b5 |= 0x08;
                if ((buttons & KEY_SELECT) != 0) b5 |= 0x10;
                if ((buttons & KEY_START)  != 0) b5 |= 0x20;

                Array.Clear(report, 0, 63);
                report[0] = ScaleStick(cpX);
                report[1] = ScaleStick((short)-cpY);
                report[2] = ScaleStick(csX);
                report[3] = ScaleStick((short)-csY);
                report[4] = b4;
                report[5] = b5;
                report[7] = (buttons & KEY_ZL) != 0 ? (byte)255 : (byte)0;
                report[8] = (buttons & KEY_ZR) != 0 ? (byte)255 : (byte)0;

                ushort ts = (ushort)(packets * 16);
                report[9]  = (byte)(ts & 0xFF);
                report[10] = (byte)(ts >> 8);
                report[11] = 0x1F; 

                report[13] = (byte)( ds4GyroX       & 0xFF);
                report[14] = (byte)((ds4GyroX >> 8) & 0xFF);
                report[15] = (byte)( ds4GyroZ       & 0xFF);
                report[16] = (byte)((ds4GyroZ >> 8) & 0xFF);
                report[17] = (byte)( ds4GyroY       & 0xFF);
                report[18] = (byte)((ds4GyroY >> 8) & 0xFF);

                short accelY = 8192; 
                report[21] = (byte)( accelY       & 0xFF);
                report[22] = (byte)((accelY >> 8) & 0xFF);

                report[35] = 0x80;
                report[38] = 0x80;

                try
                {
                    var r = vigem_target_ds4_update_ex(vc, pad, report, 63);
                    if (r != VIGEM_ERROR.VIGEM_ERROR_NONE) break;
                }
                catch { break; }

                packets++;
            }

            vigem_target_remove(vc, pad);
            vigem_target_free(pad);
            vigem_disconnect(vc);
            vigem_free(vc);
            return 0;
        }

        static void ConsoleInputLoop()
        {
            while (_running)
            {
                string? input = Console.ReadLine()?.Trim();
                if (input == "1" && !_isMenuInteracting)
                {
                    _isMenuInteracting = true;
                    TriggerGyroCalibration();
                    _isMenuInteracting = false;
                    UpdateConsoleUI();
                }
                else if (input == "2" && !_isMenuInteracting)
                {
                    _isMenuInteracting = true;
                    Console.Write($"\n  Enter a new sensitivity (now {_gyroSensitivityDivider.ToString(CultureInfo.InvariantCulture)}): ");
                    string? valInput = Console.ReadLine();
                    if (valInput != null && double.TryParse(valInput.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double res) && res > 0)
                    {
                        _gyroSensitivityDivider = res;
                    }
                    _isMenuInteracting = false;
                    UpdateConsoleUI();
                }
                else if (input == "3" && !_isMenuInteracting)
                {
                    _isMenuInteracting = true;
                    Console.Write($"\n  Enter the new dead zone (now {_gyroDeadzone.ToString(CultureInfo.InvariantCulture)}): ");
                    string? valInput = Console.ReadLine();
                    if (valInput != null && double.TryParse(valInput.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double res) && res >= 0)
                    {
                        _gyroDeadzone = res;
                    }
                    _isMenuInteracting = false;
                    UpdateConsoleUI();
                }
                else
                {
                    UpdateConsoleUI();
                }
            }
        }

        static void UpdateConsoleUI()
        {
            if (_isMenuInteracting) return;

            Console.Clear();
            PrintBanner();

            Console.Write("  Connection status:         ");
            if (_connectionStatus == "Connected")
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("CONNECTED");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("DISCONNECTED");
            }
            Console.ResetColor();

            Console.WriteLine($"  PC local IP address:       {_localIp}");
            Console.WriteLine($"  Port:                      {_port}");

            Console.Write("  Gyro calibration:          ");
            if (_calib.IsCalibrated)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("COMPLETED");
                Console.ResetColor();
                Console.WriteLine($" (Bias: X={_calib.BiasX:+0.0;-0.0} Y={_calib.BiasY:+0.0;-0.0} Z={_calib.BiasZ:+0.0;-0.0})");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("NOT CALIBRATED (Default values used)");
                Console.ResetColor();
            }

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [Current gyroscope parameters]");
            Console.ResetColor();
            
            Console.Write("  Sensitivity:               ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{_gyroSensitivityDivider.ToString("F1", CultureInfo.InvariantCulture)}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  (higher = smoother)");
            Console.ResetColor();

            Console.Write("  Dead zone:                 ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{_gyroDeadzone.ToString("F1", CultureInfo.InvariantCulture)}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  (microvibration filtration)");
            Console.ResetColor();

            Console.WriteLine("\n  ──────────────────────────────────────────────────────────");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Command menu:");
            Console.ResetColor();
            Console.WriteLine("  1. Gyro calibration");
            Console.WriteLine("  2. Change sensitivity");
            Console.WriteLine("  3. Change deadzone");
            Console.WriteLine("  ──────────────────────────────────────────────────────────");
            Console.Write("  Type option: ");
        }

        static void ResetReportToDefault(byte[] report)
        {
            Array.Clear(report, 0, report.Length);
            report[0] = 128;       
            report[1] = 128;       
            report[2] = 128;       
            report[3] = 128;       
            report[4] = DS4_DPAD_NONE; 
            report[35] = 0x80;
            report[38] = 0x80;
        }

        static void TriggerGyroCalibration()
        {
            Console.Clear();
            PrintBanner();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ╔══════════════════════════════════════╗");
            Console.WriteLine("  ║       !gyroscope calibration!        ║");
            Console.WriteLine("  ║       Place your 3DS flat and        ║");
            Console.WriteLine("  ║     don't touch it for 3 seconds.    ║");
            Console.WriteLine("  ╚══════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            _udp!.Client.ReceiveTimeout = 0;

            const int CALIB_SAMPLES = 180;
            var samplesX = new List<float>(CALIB_SAMPLES);
            var samplesY = new List<float>(CALIB_SAMPLES);
            var samplesZ = new List<float>(CALIB_SAMPLES);
            var remote = new IPEndPoint(IPAddress.Any, 0);

            Console.Write("  Progress: [");
            int lastBar = 0;

            while (_udp!.Available > 0)
            {
                try { _udp.Receive(ref remote); } catch { break; }
            }

            while (samplesX.Count < CALIB_SAMPLES && _running)
            {
                byte[] data;
                try { data = _udp!.Receive(ref remote); }
                catch { break; }

                if (data.Length == 5 && data[0] == 'p' && data[1] == 'i' && data[2] == 'n' && data[3] == 'g')
                {
                    try { _udp!.Send(Encoding.ASCII.GetBytes("pong"), 4, remote); } catch { }
                    continue;
                }

                if (data.Length < PACKET_SIZE) continue;

                samplesX.Add(BitConverter.ToInt16(data, 12));
                samplesY.Add(BitConverter.ToInt16(data, 14));
                samplesZ.Add(BitConverter.ToInt16(data, 16));

                int bar = samplesX.Count * 20 / CALIB_SAMPLES;
                while (lastBar < bar)
                {
                    Console.Write("█");
                    lastBar++;
                }
            }

            while (lastBar < 20) { Console.Write("░"); lastBar++; }
            Console.WriteLine("]");

            _udp.Client.ReceiveTimeout = 1000;

            if (samplesX.Count > 0)
            {
                _calib.BiasX = Mean(samplesX);
                _calib.BiasY = Mean(samplesY);
                _calib.BiasZ = Mean(samplesZ);
                _calib.IsCalibrated = true;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  [OK] Calibration completed successfully!");
                Console.ResetColor();
                Thread.Sleep(1500); 
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n  [ERR] No data from 3DS. Calibration failed.");
                Console.ResetColor();
                Thread.Sleep(2000);
            }
        }

        static string GetLocalIPAddress()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    string desc = ni.Description.ToLower();
                    if (desc.Contains("virtual") || desc.Contains("pseudo") || desc.Contains("wsl") || 
                        desc.Contains("hyper-v") || desc.Contains("host-only") || desc.Contains("hamachi"))
                        continue;

                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string res = ip.Address.ToString();
                            if (res.StartsWith("192.168.") || res.StartsWith("10.") || res.StartsWith("172."))
                            {
                                return res;
                            }
                        }
                    }
                }

                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                        return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        static float Mean(List<float> list)
        {
            float sum = 0;
            foreach (var v in list) sum += v;
            return sum / list.Count;
        }

        static int Clamp(int val, int min, int max)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }

        static byte ScaleStick(short val)
        {
            if (val == 0) return 128;
            int s = (val * 127 / 150) + 128;
            if (s > 255) s = 255;
            if (s < 0)   s = 0;
            return (byte)s;
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
			██╗   ██╗ █████╗ ██████╗ ██╗██████╗  ██████╗  ██████╗ ██████╗ 
			╚██╗ ██╔╝██╔══██╗██╔══██╗██║██╔══██╗██╔═══██╗██╔═══██╗██╔══██╗
			 ╚████╔╝ ███████║██████╔╝██║██║  ██║██║   ██║██║   ██║██████╔╝
			  ╚██╔╝  ██╔══██║██╔═══╝ ██║██║  ██║██║   ██║██║   ██║██╔══██╗
			   ██║   ██║  ██║██║     ██║██████╔╝╚██████╔╝╚██████╔╝██║  ██║
			   ╚═╝   ╚═╝  ╚═╝╚═╝     ╚═╝╚═════╝  ╚═════╝  ╚═════╝ ╚═╝  ╚═╝");
            Console.ResetColor();
            Console.WriteLine("  Nintendo 3DS → DS4 Controller");
            Console.WriteLine();
        }

        static void Pause() { Console.WriteLine("  Press any key to exit..."); Console.ReadKey(); }
    }
}