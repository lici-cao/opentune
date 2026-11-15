using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("OpenTune 调优台")]
[assembly: System.Reflection.AssemblyDescription("Windows 手感与延迟调优工具箱（开源版）")]
[assembly: System.Reflection.AssemblyProduct("OpenTune")]
[assembly: System.Reflection.AssemblyCompany("lici-cao")]
[assembly: System.Reflection.AssemblyCopyright("\u00a9 2026 lici-cao \u00b7 github.com/lici-cao")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace OpenTune
{
    internal static class Meta
    {
        public const string Title = "OpenTune 调优台";
        public const string Version = "1.0.0";
        public const string Author = "lici-cao";
        public const string Home = "https://github.com/lici-cao";
    }

    internal sealed class TweakItem
    {
        public string Name;
        public RegistryHive Hive;
        public string KeyPath;
        public string ValueName;
        public object Value;
        public RegistryValueKind Kind;
        public bool NeedAdmin;
        public string Note;

        public TweakItem(string name, RegistryHive hive, string keyPath, string valueName, object value, RegistryValueKind kind, bool needAdmin, string note)
        {
            Name = name; Hive = hive; KeyPath = keyPath; ValueName = valueName;
            Value = value; Kind = kind; NeedAdmin = needAdmin; Note = note;
        }

        public override string ToString()
        {
            return (NeedAdmin ? "[需管理员] " : "") + Name;
        }
    }

    internal static class Tweaks
    {
        public const string MultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

        public static List<TweakItem> All()
        {
            List<TweakItem> l = new List<TweakItem>();
            l.Add(new TweakItem("前台程序优先（Win32PrioritySeparation=38）", RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38,
                RegistryValueKind.DWord, true, "让前台窗口获得更集中的 CPU 时间片"));
            l.Add(new TweakItem("关闭 Xbox Game DVR 后台录制（当前用户）", RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_Enabled", 0,
                RegistryValueKind.DWord, false, "减少后台编码占用"));
            l.Add(new TweakItem("关闭 Game DVR 策略（全机）", RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0,
                RegistryValueKind.DWord, true, "对应组策略项，需管理员"));
            l.Add(new TweakItem("多媒体调度响应度 SystemResponsiveness=10", RegistryHive.LocalMachine,
                MultimediaProfile, "SystemResponsiveness", 10,
                RegistryValueKind.DWord, true, "默认 20，降低后多媒体线程让步更少"));
            l.Add(new TweakItem("解除网络限流 NetworkThrottlingIndex=0xFFFFFFFF", RegistryHive.LocalMachine,
                MultimediaProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF),
                RegistryValueKind.DWord, true, "关闭多媒体网络节流"));
            l.Add(new TweakItem("LargeSystemCache=0（桌面场景）", RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 0,
                RegistryValueKind.DWord, true, "桌面场景保持 0"));
            l.Add(new TweakItem("视觉特效设置为最佳性能", RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2,
                RegistryValueKind.DWord, false, "重启资源管理器生效"));
            return l;
        }
    }

    internal static class Reg
    {
        public static RegistryKey Base(RegistryHive hive)
        {
            return RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        }

        public static object Read(RegistryHive hive, string path, string name)
        {
            try
            {
                using (RegistryKey b = Base(hive))
                using (RegistryKey k = b.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    return k.GetValue(name, null);
                }
            }
            catch { return null; }
        }

        public static void Write(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
        {
            using (RegistryKey b = Base(hive))
            using (RegistryKey k = b.CreateSubKey(path, true))
            {
                if (k == null) throw new Exception("无法打开注册表项 " + path);
                if (kind == RegistryValueKind.DWord) k.SetValue(name, Convert.ToInt32(value), RegistryValueKind.DWord);
                else if (kind == RegistryValueKind.String) k.SetValue(name, Convert.ToString(value), RegistryValueKind.String);
                else k.SetValue(name, value, kind);
            }
        }

        public static void Delete(RegistryHive hive, string path, string name)
        {
            using (RegistryKey b = Base(hive))
            using (RegistryKey k = b.OpenSubKey(path, true))
            {
                if (k != null) k.DeleteValue(name, false);
            }
        }
    }

    internal static class BackupStore
    {
        public const string Header = "OpenTune-Backup-v1";

        public static string Dir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTune", "backup");
        }

        public static string Create()
        {
            Directory.CreateDirectory(Dir());
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine("TIME|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("POWER|" + Power.GetActiveScheme());

            List<TweakItem> all = Tweaks.All();
            foreach (TweakItem t in all)
            {
                object cur = Reg.Read(t.Hive, t.KeyPath, t.ValueName);
                if (cur == null) continue;
                string type = t.Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                sb.AppendLine("REG|" + type + "|" + t.KeyPath + "|" + t.ValueName + "|" + (cur is int ? ((int)cur).ToString() : cur.ToString()));
            }

            string file = Path.Combine(Dir(), "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".otbak");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }

        public static string Latest()
        {
            try
            {
                string[] files = Directory.GetFiles(Dir(), "*.otbak");
                if (files.Length == 0) return null;
                return files.OrderByDescending(f => f).First();
            }
            catch { return null; }
        }

        public static int Restore(string file, StringBuilder log)
        {
            if (!File.Exists(file)) throw new Exception("备份文件不存在：" + file);
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            int n = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line == Header || line.StartsWith("TIME|")) continue;
                string[] p = line.Split('|');
                if (p[0] == "POWER" && p.Length >= 2)
                {
                    Power.SetActiveScheme(p[1]);
                    n++;
                    log.AppendLine("[还原] 电源计划 -> " + p[1]);
                    continue;
                }
                if (p[0] == "REG" && p.Length >= 5)
                {
                    RegistryHive hive = p[1] == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                    int val;
                    if (int.TryParse(p[4], out val))
                    {
                        Reg.Write(hive, p[2], p[3], val, RegistryValueKind.DWord);
                    }
                    else
                    {
                        Reg.Write(hive, p[2], p[3], p[4], RegistryValueKind.String);
                    }
                    n++;
                    log.AppendLine("[还原] " + p[1] + "\\" + p[2] + " · " + p[3] + " = " + p[4]);
                }
            }
            return n;
        }
    }

    internal static class Power
    {
        public const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public static string Run(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    return o;
                }
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }

        public static string GetActiveScheme()
        {
            string o = Run("/getactivescheme");
            int i = o.IndexOf("GUID:");
            if (i < 0) return "";
            string s = o.Substring(i + 5).Trim();
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp).Trim();
            return s.TrimEnd(')', '(').Trim();
        }

        public static bool SetActiveScheme(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            string o = Run("/setactive " + guid);
            return o.IndexOf("ERR", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    internal static class TimerRes
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desired, bool set, out uint current);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint min, out uint max, out uint current);

        public static string Query()
        {
            uint min, max, cur;
            int st = NtQueryTimerResolution(out min, out max, out cur);
            if (st != 0) return "查询失败";
            return string.Format("{0:F2} ms / 最小 {1:F2} ms", cur / 10000.0, max / 10000.0);
        }

        public static bool SetMs(double ms)
        {
            uint desired = (uint)Math.Round(ms * 10000.0);
            uint cur;
            int st = NtSetTimerResolution(desired, true, out cur);
            return st == 0;
        }
    }

    internal sealed class CleanTarget
    {
        public string Name;
        public string Path;
        public string Filter;
        public CleanTarget(string name, string path, string filter)
        {
            Name = name; Path = path; Filter = filter;
        }
        public long Size()
        {
            try
            {
                if (!Directory.Exists(Path)) return 0;
                long total = 0;
                string[] files = Directory.GetFiles(Path, string.IsNullOrEmpty(Filter) ? "*" : Filter, SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }
        public int Clean()
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(Path)) return 0;
                string[] files = Directory.GetFiles(Path, string.IsNullOrEmpty(Filter) ? "*" : Filter, SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    try { File.Delete(f); n++; } catch { }
                }
                string[] dirs = Directory.GetDirectories(Path);
                foreach (string d in dirs)
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
            return n;
        }
    }

    internal static class Cleaners
    {
        public static List<CleanTarget> All()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string temp = Path.GetTempPath();
            List<CleanTarget> l = new List<CleanTarget>();
            l.Add(new CleanTarget("DirectX 着色器缓存（D3DSCache）", Path.Combine(local, "D3DSCache"), ""));
            l.Add(new CleanTarget("NVIDIA DX 着色器缓存", Path.Combine(local, "NVIDIA", "DXCache"), ""));
            l.Add(new CleanTarget("NVIDIA OpenGL 缓存", Path.Combine(local, "NVIDIA", "GLCache"), ""));
            l.Add(new CleanTarget("AMD DX 着色器缓存", Path.Combine(local, "AMD", "DxCache"), ""));
            l.Add(new CleanTarget("AMD DX 编译器缓存", Path.Combine(local, "AMD", "DxcCache"), ""));
            l.Add(new CleanTarget("AMD OpenGL 缓存", Path.Combine(local, "AMD", "GLCache"), ""));
            l.Add(new CleanTarget("缩略图缓存（thumbcache）", Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db"));
            l.Add(new CleanTarget("用户临时文件（%TEMP%）", temp, ""));
            return l;
        }
    }

    internal static class SysInfo
    {
        public static string Get(string wql, string prop)
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM " + wql))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        object v = o[prop];
                        if (v != null) return Convert.ToString(v).Trim();
                    }
                }
            }
            catch { }
            return "未知";
        }

        public static string[] GetMany(string wql, string prop)
        {
            List<string> l = new List<string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM " + wql))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        object v = o[prop];
                        if (v != null) l.Add(Convert.ToString(v).Trim());
                    }
                }
            }
            catch { }
            return l.ToArray();
        }

        public static string Summary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString + "  (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")");
            sb.AppendLine("CPU：" + Get("Win32_Processor", "Name") + " / " + Environment.ProcessorCount + " 逻辑处理器");
            string[] gpus = GetMany("Win32_VideoController", "Name");
            sb.AppendLine("显卡：" + (gpus.Length == 0 ? "未知" : string.Join(" + ", gpus)));
            sb.AppendLine("内存：" + Get("Win32_ComputerSystem", "TotalPhysicalMemory"));
            sb.AppendLine("主板：" + Get("Win32_BaseBoard", "Product"));
            sb.AppendLine("电源计划：" + Power.GetActiveScheme());
            sb.AppendLine("计时器分辨率：" + TimerRes.Query());
            return sb.ToString();
        }
    }

    internal static class Monitors
    {
        private static PerformanceCounter cpuCounter;
        private static PerformanceCounter dpcCounter;
        private static bool inited = false;

        public static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                if (PerformanceCounterCategory.Exists("Processor"))
                {
                    cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    cpuCounter.NextValue();
                }
            }
            catch { cpuCounter = null; }
            try
            {
                if (PerformanceCounterCategory.Exists("Processor"))
                {
                    dpcCounter = new PerformanceCounter("Processor", "% DPC Time", "_Total");
                    dpcCounter.NextValue();
                }
            }
            catch { dpcCounter = null; }
        }

        public static double Cpu()
        {
            try { if (cpuCounter == null) return -1; return Math.Round(cpuCounter.NextValue(), 1); }
            catch { return -1; }
        }

        public static double Dpc()
        {
            try { if (dpcCounter == null) return -1; return Math.Round(dpcCounter.NextValue(), 1); }
            catch { return -1; }
        }

        public static double MemPercent()
        {
            try
            {
                ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject o in s.Get())
                {
                    double total = Convert.ToDouble(o["TotalVisibleMemorySize"]);
                    double free = Convert.ToDouble(o["FreePhysicalMemory"]);
                    return Math.Round((total - free) / total * 100.0, 1);
                }
            }
            catch { }
            return -1;
        }

        public static double Gpu3D()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine")) return -1;
                PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
                string[] names = cat.GetInstanceNames();
                double sum = 0;
                int used = 0;
                foreach (string n in names)
                {
                    if (n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (used > 400) break;
                    try
                    {
                        PerformanceCounter pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                        sum += pc.NextValue();
                        pc.Dispose();
                        used++;
                    }
                    catch { }
                }
                if (sum > 100) sum = 100;
                return Math.Round(sum, 1);
            }
            catch { return -1; }
        }
    }

    internal static class SpeedTest
    {
        public static string Download(int megabytes, out double seconds)
        {
            seconds = 0;
            string url = "https://speed.cloudflare.com/__down?bytes=" + (megabytes * 1024 * 1024);
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; }
                catch { }
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 16;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 60000;
                req.ReadWriteTimeout = 60000;
                req.UserAgent = "OpenTune/" + Meta.Version;
                Stopwatch sw = Stopwatch.StartNew();
                long total = 0;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                {
                    byte[] buf = new byte[131072];
                    int r;
                    while ((r = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += r;
                        if (sw.Elapsed.TotalSeconds > 60) break;
                    }
                }
                sw.Stop();
                seconds = sw.Elapsed.TotalSeconds;
                if (seconds <= 0) seconds = 0.001;
                double mbps = (total * 8.0 / 1000000.0) / seconds;
                return string.Format("下载 {0:F1} MB / {1:F2} s → {2:F1} Mbps", total / 1048576.0, seconds, mbps);
            }
            catch (Exception ex)
            {
                return "测速失败：" + ex.Message;
            }
        }

        public static string PingHost(string host, int count)
        {
            try
            {
                using (Ping p = new Ping())
                {
                    long sum = 0;
                    int ok = 0;
                    long min = long.MaxValue;
                    long max = 0;
                    for (int i = 0; i < count; i++)
                    {
                        PingReply r = p.Send(host, 3000);
                        if (r != null && r.Status == IPStatus.Success)
                        {
                            ok++;
                            sum += r.RoundtripTime;
                            if (r.RoundtripTime < min) min = r.RoundtripTime;
                            if (r.RoundtripTime > max) max = r.RoundtripTime;
                        }
                        Thread.Sleep(200);
                    }
                    if (ok == 0) return host + "：全部超时";
                    return string.Format("{0}：发送 {1} / 成功 {2} / 平均 {3:F0} ms / 最小 {4} ms / 最大 {5} ms", host, count, ok, sum / (double)ok, min, max);
                }
            }
            catch (Exception ex) { return host + "：失败 " + ex.Message; }
        }
    }

    internal static class SelfTest
    {
        public static void Run(StringBuilder sb)
        {
            sb.AppendLine("== OpenTune " + Meta.Version + " selftest ==");
            sb.AppendLine("author: " + Meta.Author + "  home: " + Meta.Home);
            sb.AppendLine("admin: " + Program.IsAdmin());
            sb.AppendLine("--- 系统 ---");
            sb.AppendLine(SysInfo.Summary());

            sb.AppendLine("--- 调优项读取 ---");
            foreach (TweakItem t in Tweaks.All())
            {
                object cur = Reg.Read(t.Hive, t.KeyPath, t.ValueName);
                sb.AppendLine((cur == null ? "(未设置) " : cur + " ") + t.Name);
            }

            sb.AppendLine("--- 备份 ---");
            string f = BackupStore.Create();
            sb.AppendLine("created: " + f + "  bytes=" + new FileInfo(f).Length);
            StringBuilder rl = new StringBuilder();
            int n = BackupStore.Restore(f, rl);
            sb.AppendLine("restore items=" + n);
            sb.AppendLine(rl.ToString().Trim());

            sb.AppendLine("--- 清理统计 ---");
            long total = 0;
            foreach (CleanTarget c in Cleaners.All())
            {
                long sz = c.Size();
                total += sz;
                sb.AppendLine(string.Format("{0} : {1:F1} MB  ({2})", c.Name, sz / 1048576.0, c.Path));
            }
            sb.AppendLine("合计可清理 " + (total / 1048576.0).ToString("F1") + " MB");

            sb.AppendLine("--- 监控 ---");
            Monitors.Init();
            Thread.Sleep(1100);
            sb.AppendLine("CPU " + Monitors.Cpu() + "% / DPC " + Monitors.Dpc() + "% / 内存 " + Monitors.MemPercent() + "%");
            sb.AppendLine("GPU(3D) " + Monitors.Gpu3D() + "%");
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    string[] inst = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
                    int n3d = 0;
                    foreach (string s in inst) if (s.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0) n3d++;
                    sb.AppendLine("GPU引擎实例总数=" + inst.Length + " / 3D实例=" + n3d);
                }
                else sb.AppendLine("GPU引擎计数器不可用");
            }
            catch (Exception ex) { sb.AppendLine("GPU实例枚举失败: " + ex.Message); }

            sb.AppendLine("--- 网络 ---");
            sb.AppendLine(SpeedTest.PingHost("1.1.1.1", 4));
            double sec;
            sb.AppendLine(SpeedTest.Download(8, out sec));
            sb.AppendLine("== selftest done ==");
        }
    }

    internal static class Program
    {
        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                string outFile = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "opentune-selftest.log");
                StringBuilder sb = new StringBuilder();
                try { SelfTest.Run(sb); }
                catch (Exception ex) { sb.AppendLine("FAIL " + ex.ToString()); }
                try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private TabControl tabs;
        private CheckedListBox tweaksList;
        private CheckBox chkPowerHigh;
        private CheckBox chkTimer;
        private CheckBox chkFlushDns;
        private TextBox log;
        private Label lblMon;
        private Label lblSys;
        private CheckedListBox cleanList;
        private ListBox procList;
        private ComboBox cbPriority;
        private TextBox txtAffinity;
        private TextBox txtSpeed;
        private System.Windows.Forms.Timer monTimer;
        private bool busy = false;

        public MainForm()
        {
            Text = Meta.Title + " v" + Meta.Version + "  ·  by " + Meta.Author;
            Width = 980;
            Height = 690;
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(28, 30, 34);
            ForeColor = Color.Gainsboro;
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();
            Log("OpenTune 调优台 v" + Meta.Version + "  ·  作者 " + Meta.Author + "  ·  " + Meta.Home);
            Log(Program.IsAdmin() ? "已以管理员身份运行，全部优化项可用" : "当前非管理员，带 [需管理员] 的项将写入失败，建议右键以管理员身份运行");
            Log("每次修改前自动生成备份，可在「概览」页一键还原");
            monTimer = new System.Windows.Forms.Timer();
            monTimer.Interval = 1500;
            monTimer.Tick += MonTimerTick;
            monTimer.Start();
        }

        private void BuildUi()
        {
            tabs = new TabControl();
            tabs.Dock = DockStyle.Top;
            tabs.Height = 470;
            tabs.Font = new Font("Microsoft YaHei UI", 9.5F);

            tabs.TabPages.Add(BuildOverview());
            tabs.TabPages.Add(BuildProcess());
            tabs.TabPages.Add(BuildClean());
            tabs.TabPages.Add(BuildMonitor());
            tabs.TabPages.Add(BuildSpeed());
            tabs.TabPages.Add(BuildAbout());

            log = new TextBox();
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.Dock = DockStyle.Fill;
            log.BackColor = Color.FromArgb(20, 22, 26);
            log.ForeColor = Color.FromArgb(170, 220, 180);
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9F);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.Padding = new Padding(8);
            bottom.Controls.Add(log);

            Controls.Add(bottom);
            Controls.Add(tabs);
        }

        private Button MkButton(string text, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Height = 30;
            b.Width = 120;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(52, 58, 68);
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 88, 100);
            if (h != null) b.Click += h;
            return b;
        }

        private TabPage BuildOverview()
        {
            TabPage p = new TabPage("概览 / 一键优化");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            lblSys = new Label();
            lblSys.Left = 12; lblSys.Top = 10; lblSys.Width = 920; lblSys.Height = 120;
            lblSys.Font = new Font("Microsoft YaHei UI", 9F);
            lblSys.Text = "读取硬件信息…";

            tweaksList = new CheckedListBox();
            tweaksList.Left = 12; tweaksList.Top = 135; tweaksList.Width = 920; tweaksList.Height = 170;
            tweaksList.BackColor = Color.FromArgb(24, 26, 30);
            tweaksList.ForeColor = Color.Gainsboro;
            tweaksList.CheckOnClick = true;
            tweaksList.HorizontalScrollbar = true;
            foreach (TweakItem t in Tweaks.All()) tweaksList.Items.Add(t);

            chkPowerHigh = new CheckBox();
            chkPowerHigh.Text = "电源计划切换为「高性能」";
            chkPowerHigh.Left = 12; chkPowerHigh.Top = 312; chkPowerHigh.Width = 260;

            chkTimer = new CheckBox();
            chkTimer.Text = "请求 0.5 ms 计时器分辨率（进程内）";
            chkTimer.Left = 290; chkTimer.Top = 312; chkTimer.Width = 300;

            chkFlushDns = new CheckBox();
            chkFlushDns.Text = "刷新 DNS 解析缓存";
            chkFlushDns.Left = 605; chkFlushDns.Top = 312; chkFlushDns.Width = 200; chkFlushDns.Checked = true;

            Button bApply = MkButton("执行所选优化", BtnApplyClick);
            bApply.Left = 12; bApply.Top = 345;
            Button bBak = MkButton("立即备份", BtnBackupClick);
            bBak.Left = 142; bBak.Top = 345;
            Button bRes = MkButton("还原最近备份", BtnRestoreClick);
            bRes.Left = 272; bRes.Top = 345; bRes.Width = 140;
            Button bSel = MkButton("全部勾选", delegate { for (int i = 0; i < tweaksList.Items.Count; i++) tweaksList.SetItemChecked(i, true); });
            bSel.Left = 422; bSel.Top = 345;
            Button bUnsel = MkButton("全部取消", delegate { for (int i = 0; i < tweaksList.Items.Count; i++) tweaksList.SetItemChecked(i, false); });
            bUnsel.Left = 552; bUnsel.Top = 345;

            p.Controls.Add(lblSys);
            p.Controls.Add(tweaksList);
            p.Controls.Add(chkPowerHigh);
            p.Controls.Add(chkTimer);
            p.Controls.Add(chkFlushDns);
            p.Controls.Add(bApply);
            p.Controls.Add(bBak);
            p.Controls.Add(bRes);
            p.Controls.Add(bSel);
            p.Controls.Add(bUnsel);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string s = SysInfo.Summary();
                try { BeginInvoke(new Action(delegate { lblSys.Text = s; })); } catch { }
            });

            return p;
        }

        private TabPage BuildProcess()
        {
            TabPage p = new TabPage("进程优先级 / 亲和");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            procList = new ListBox();
            procList.Left = 12; procList.Top = 10; procList.Width = 460; procList.Height = 380;
            procList.BackColor = Color.FromArgb(24, 26, 30);
            procList.ForeColor = Color.Gainsboro;

            cbPriority = new ComboBox();
            cbPriority.Left = 490; cbPriority.Top = 10; cbPriority.Width = 200;
            cbPriority.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPriority.Items.AddRange(new object[] { "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime" });
            cbPriority.SelectedIndex = 3;

            txtAffinity = new TextBox();
            txtAffinity.Left = 490; txtAffinity.Top = 50; txtAffinity.Width = 200;

            Button bRefresh = MkButton("刷新进程", delegate { RefreshProcs(); });
            bRefresh.Left = 490; bRefresh.Top = 90;
            Button bApply = MkButton("应用设置", BtnProcApplyClick);
            bApply.Left = 490; bApply.Top = 130; bApply.Width = 200;

            Label tip = new Label();
            tip.Left = 490; tip.Top = 170; tip.Width = 430; tip.Height = 220;
            tip.Text = "选择左侧进程后：\r\n1) 下拉框设置优先级类；\r\n2) 亲和掩码为十六进制（留空=全部核心，如 0F = 前 4 核）；\r\n3) 点击应用。\r\n\r\n注意：系统关键进程不允许修改；RealTime 可能造成系统失去响应，谨慎使用。";
            tip.ForeColor = Color.Silver;

            p.Controls.Add(procList);
            p.Controls.Add(cbPriority);
            p.Controls.Add(txtAffinity);
            p.Controls.Add(bRefresh);
            p.Controls.Add(bApply);
            p.Controls.Add(tip);
            return p;
        }

        private TabPage BuildClean()
        {
            TabPage p = new TabPage("缓存清理");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            cleanList = new CheckedListBox();
            cleanList.Left = 12; cleanList.Top = 10; cleanList.Width = 900; cleanList.Height = 300;
            cleanList.BackColor = Color.FromArgb(24, 26, 30);
            cleanList.ForeColor = Color.Gainsboro;
            cleanList.CheckOnClick = true;
            cleanList.HorizontalScrollbar = true;
            foreach (CleanTarget c in Cleaners.All()) cleanList.Items.Add(c);

            Button bScan = MkButton("统计占用", delegate { ScanClean(); });
            bScan.Left = 12; bScan.Top = 320;
            Button bClean = MkButton("清理所选", BtnCleanClick);
            bClean.Left = 142; bClean.Top = 320; bClean.Width = 140;

            Label tip = new Label();
            tip.Left = 12; tip.Top = 356; tip.Width = 900;
            tip.Text = "仅删除下列白名单目录内的缓存文件：D3DSCache、NVIDIA/AMD 着色器缓存、缩略图缓存、%TEMP%。不触碰用户文档与游戏本体。";
            tip.ForeColor = Color.Silver;

            p.Controls.Add(cleanList);
            p.Controls.Add(bScan);
            p.Controls.Add(bClean);
            p.Controls.Add(tip);
            return p;
        }

        private TabPage BuildMonitor()
        {
            TabPage p = new TabPage("实时监控");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            lblMon = new Label();
            lblMon.Left = 12; lblMon.Top = 10; lblMon.Width = 900; lblMon.Height = 330;
            lblMon.Font = new Font("Consolas", 12F);
            lblMon.Text = "读取中…";

            Button bProbe = MkButton("DPC 采样", delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("DPC / CPU 采样（1 秒一次，共 5 次）：");
                    for (int i = 0; i < 5; i++)
                    {
                        sb.AppendLine(string.Format("  #{0}  CPU {1:F1}%   DPC {2:F1}%   GPU {3:F1}%", i + 1, Monitors.Cpu(), Monitors.Dpc(), Monitors.Gpu3D()));
                        Thread.Sleep(1000);
                    }
                    AppendLog(sb.ToString());
                });
            });
            bProbe.Left = 12; bProbe.Top = 350; bProbe.Width = 140;

            Button bRes = MkButton("恢复 1 ms 计时器", delegate
            {
                bool ok = TimerRes.SetMs(1.0);
                Log(ok ? "已请求 1.00 ms 计时器分辨率" : "计时器分辨率请求失败");
            });
            bRes.Left = 162; bRes.Top = 350; bRes.Width = 160;

            p.Controls.Add(lblMon);
            p.Controls.Add(bProbe);
            p.Controls.Add(bRes);
            return p;
        }

        private TabPage BuildSpeed()
        {
            TabPage p = new TabPage("网络测速");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            txtSpeed = new TextBox();
            txtSpeed.Left = 12; txtSpeed.Top = 10; txtSpeed.Width = 900; txtSpeed.Height = 300;
            txtSpeed.Multiline = true;
            txtSpeed.ScrollBars = ScrollBars.Vertical;
            txtSpeed.BackColor = Color.FromArgb(24, 26, 30);
            txtSpeed.ForeColor = Color.Gainsboro;
            txtSpeed.Font = new Font("Consolas", 9.5F);
            txtSpeed.Text = "点击下方按钮开始：延迟测试（1.1.1.1）+ 下载测速（Cloudflare 25 MB）";

            Button bPing = MkButton("延迟测试", delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string r = SpeedTest.PingHost("1.1.1.1", 10);
                    AppendSpeed(r);
                });
            });
            bPing.Left = 12; bPing.Top = 320;

            Button bDown = MkButton("下载测速", delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    AppendSpeed("测速中…（25 MB）");
                    double sec;
                    string r = SpeedTest.Download(25, out sec);
                    AppendSpeed(r);
                });
            });
            bDown.Left = 142; bDown.Top = 320;

            p.Controls.Add(txtSpeed);
            p.Controls.Add(bPing);
            p.Controls.Add(bDown);
            return p;
        }

        private TabPage BuildAbout()
        {
            TabPage p = new TabPage("关于");
            p.BackColor = Color.FromArgb(32, 34, 40);
            p.ForeColor = Color.Gainsboro;

            Label l = new Label();
            l.Left = 16; l.Top = 14; l.Width = 900; l.Height = 390;
            l.Text = Meta.Title + " v" + Meta.Version + "\r\n\r\n" +
                "作者：lici-cao\r\n" +
                "主页：github.com/lici-cao\r\n" +
                "许可：MIT License（可自由使用、修改、分发，保留署名即可）\r\n\r\n" +
                "定位：面向游戏手感与延迟的 Windows 调优与清理工具，全部操作本地完成，不联网上传任何数据。\r\n\r\n" +
                "内置能力：\r\n" +
                "  · 一键优化（前台优先级、Game DVR、多媒体调度、网络限流等注册表项）\r\n" +
                "  · 电源计划切换与计时器分辨率调节\r\n" +
                "  · 注册表 / 电源计划自动备份与一键还原\r\n" +
                "  · 进程优先级与 CPU 亲和设置\r\n" +
                "  · 着色器缓存、缩略图缓存、临时文件清理（白名单）\r\n" +
                "  · CPU / DPC / 内存 / GPU(3D) 实时监控\r\n" +
                "  · 延迟与下载测速（Cloudflare）\r\n\r\n" +
                "免责声明：修改系统参数存在风险，工具会在每次修改前自动备份，建议先用「立即备份」。";
            l.ForeColor = Color.Gainsboro;

            p.Controls.Add(l);
            return p;
        }

        private void AppendLog(string s)
        {
            try
            {
                if (log.InvokeRequired) { log.BeginInvoke(new Action(delegate { AppendLog(s); })); return; }
                log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
            }
            catch { }
        }

        private void AppendSpeed(string s)
        {
            try
            {
                if (txtSpeed.InvokeRequired) { txtSpeed.BeginInvoke(new Action(delegate { AppendSpeed(s); })); return; }
                txtSpeed.Text = s + "\r\n\r\n" + txtSpeed.Text;
            }
            catch { }
        }

        private void Log(string s) { AppendLog(s); }

        private void MonTimerTick(object sender, EventArgs e)
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string s = string.Format(
                        "CPU    {0,6:F1} %\r\nDPC    {1,6:F1} %\r\n内存   {2,6:F1} %\r\nGPU3D  {3,6:F1} %\r\n计时器 {4}",
                        Monitors.Cpu(), Monitors.Dpc(), Monitors.MemPercent(), Monitors.Gpu3D(), TimerRes.Query());
                    try { BeginInvoke(new Action(delegate { lblMon.Text = s; })); } catch { }
                }
                finally { busy = false; }
            });
        }

        private void BtnBackupClick(object sender, EventArgs e)
        {
            try
            {
                string f = BackupStore.Create();
                Log("[备份完成] " + f);
                MessageBox.Show("备份已生成：\r\n" + f, Meta.Title);
            }
            catch (Exception ex) { Log("[备份失败] " + ex.Message); }
        }

        private void BtnRestoreClick(object sender, EventArgs e)
        {
            string f = BackupStore.Latest();
            if (f == null) { MessageBox.Show("没有找到备份文件。", Meta.Title); return; }
            if (MessageBox.Show("将从最近备份还原：\r\n" + f + "\r\n\r\n继续？", Meta.Title, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                int n = BackupStore.Restore(f, sb);
                Log("[还原完成] 共 " + n + " 项");
                Log(sb.ToString().Trim());
            }
            catch (Exception ex) { Log("[还原失败] " + ex.Message); }
        }

        private void BtnApplyClick(object sender, EventArgs e)
        {
            List<TweakItem> sel = new List<TweakItem>();
            for (int i = 0; i < tweaksList.Items.Count; i++)
            {
                if (tweaksList.GetItemChecked(i)) sel.Add((TweakItem)tweaksList.Items[i]);
            }
            if (sel.Count == 0 && !chkPowerHigh.Checked && !chkTimer.Checked && !chkFlushDns.Checked)
            {
                MessageBox.Show("没有勾选任何项目。", Meta.Title);
                return;
            }
            try
            {
                string bak = BackupStore.Create();
                Log("[备份] " + bak);
            }
            catch (Exception ex) { Log("[备份失败，已中止] " + ex.Message); return; }

            int ok = 0, fail = 0;
            foreach (TweakItem t in sel)
            {
                try
                {
                    Reg.Write(t.Hive, t.KeyPath, t.ValueName, t.Value, t.Kind);
                    ok++;
                    Log("[OK] " + t.Name);
                }
                catch (Exception ex)
                {
                    fail++;
                    Log("[失败] " + t.Name + " → " + ex.Message);
                }
            }

            if (chkPowerHigh.Checked)
            {
                bool r = Power.SetActiveScheme(Power.HighPerf);
                Log(r ? "[OK] 电源计划 → 高性能" : "[失败] 电源计划切换失败");
                if (r) ok++; else fail++;
            }
            if (chkTimer.Checked)
            {
                bool r = TimerRes.SetMs(0.5);
                Log(r ? "[OK] 计时器分辨率 → 0.5 ms（进程退出后失效）" : "[失败] 计时器分辨率请求失败");
            }
            if (chkFlushDns.Checked)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("ipconfig.exe", "/flushdns");
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    Process.Start(psi).WaitForExit(4000);
                    Log("[OK] 已刷新 DNS 缓存");
                }
                catch (Exception ex) { Log("[失败] DNS 刷新：" + ex.Message); }
            }

            Log(string.Format("执行结束：成功 {0} 项，失败 {1} 项。部分项需重启或注销后生效。", ok, fail));
        }

        private void RefreshProcs()
        {
            procList.Items.Clear();
            try
            {
                Process[] ps = Process.GetProcesses();
                Array.Sort(ps, delegate (Process a, Process b) { return string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase); });
                foreach (Process pr in ps)
                {
                    try
                    {
                        procList.Items.Add(pr.ProcessName + "  [" + pr.Id + "]  内存 " + (pr.WorkingSet64 / 1048576) + " MB");
                    }
                    catch { }
                }
                Log("已列出 " + procList.Items.Count + " 个进程");
            }
            catch (Exception ex) { Log("枚举进程失败：" + ex.Message); }
        }

        private void BtnProcApplyClick(object sender, EventArgs e)
        {
            if (procList.SelectedIndex < 0) { MessageBox.Show("先在左侧选择一个进程。", Meta.Title); return; }
            string s = Convert.ToString(procList.SelectedItem);
            int lb = s.IndexOf('[');
            int rb = s.IndexOf(']');
            if (lb < 0 || rb <= lb) return;
            int pid;
            if (!int.TryParse(s.Substring(lb + 1, rb - lb - 1), out pid)) return;
            try
            {
                Process pr = Process.GetProcessById(pid);
                string pri = Convert.ToString(cbPriority.SelectedItem);
                ProcessPriorityClass pc = ProcessPriorityClass.Normal;
                if (pri == "Idle") pc = ProcessPriorityClass.Idle;
                else if (pri == "BelowNormal") pc = ProcessPriorityClass.BelowNormal;
                else if (pri == "AboveNormal") pc = ProcessPriorityClass.AboveNormal;
                else if (pri == "High") pc = ProcessPriorityClass.High;
                else if (pri == "RealTime") pc = ProcessPriorityClass.RealTime;
                pr.PriorityClass = pc;
                Log("[OK] " + pr.ProcessName + " 优先级 → " + pri);

                string mask = txtAffinity.Text.Trim();
                if (mask.Length > 0)
                {
                    IntPtr v = new IntPtr(Convert.ToInt64(mask, 16));
                    pr.ProcessorAffinity = v;
                    Log("[OK] " + pr.ProcessName + " 亲和掩码 → 0x" + mask.ToUpper());
                }
            }
            catch (Exception ex) { Log("[失败] " + ex.Message); }
        }

        private void ScanClean()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < cleanList.Items.Count; i++)
                {
                    object it = cleanList.Items[i];
                    CleanShow cs = it as CleanShow;
                    CleanTarget c = cs != null ? cs.Target : (CleanTarget)it;
                    long sz = c.Size();
                    int idx = i;
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            cleanList.Items[idx] = new CleanShow(c, sz);
                        }));
                    }
                    catch { }
                }
                AppendLog("缓存占用统计完成");
            });
        }

        private void BtnCleanClick(object sender, EventArgs e)
        {
            List<CleanTarget> sel = new List<CleanTarget>();
            for (int i = 0; i < cleanList.Items.Count; i++)
            {
                if (!cleanList.GetItemChecked(i)) continue;
                object it = cleanList.Items[i];
                CleanShow cs = it as CleanShow;
                sel.Add(cs != null ? cs.Target : (CleanTarget)it);
            }
            if (sel.Count == 0) { MessageBox.Show("没有勾选清理项。", Meta.Title); return; }
            long total = 0;
            foreach (CleanTarget c in sel) total += c.Size();
            if (MessageBox.Show("将清理约 " + (total / 1048576.0).ToString("F1") + " MB 缓存，继续？", Meta.Title, MessageBoxButtons.OKCancel) != DialogResult.OK) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (CleanTarget c in sel)
                {
                    int n = c.Clean();
                    AppendLog("[清理] " + c.Name + " 删除 " + n + " 个文件");
                }
                AppendLog("清理完成");
            });

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ipconfig.exe", "/flushdns");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                Process.Start(psi).WaitForExit(4000);
                Log("[OK] 已刷新 DNS 缓存");
            }
            catch { }
        }
    }

    internal sealed class CleanShow
    {
        public CleanTarget Target;
        public long Size;
        public CleanShow(CleanTarget t, long s) { Target = t; Size = s; }
        public override string ToString()
        {
            return string.Format("{0}  —  {1:F1} MB  ({2})", Target.Name, Size / 1048576.0, Target.Path);
        }
    }
}
