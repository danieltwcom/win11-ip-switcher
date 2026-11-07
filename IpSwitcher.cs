using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Win11IpSwitcher
{
    public class AppConfig
    {
        public string AdapterName { get; set; }
        public string IpAddress { get; set; }
        public string SubnetMask { get; set; }
        public string Gateway { get; set; }
        public string PrimaryDns { get; set; }
        public string SecondaryDns { get; set; }

        public AppConfig()
        {
            AdapterName = "";
            IpAddress = "";
            SubnetMask = "255.255.255.0";
            Gateway = "";
            PrimaryDns = "";
            SecondaryDns = "";
        }

        public static AppConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new AppConfig();
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppConfig();
                }
                var serializer = new JavaScriptSerializer();
                var cfg = serializer.Deserialize<AppConfig>(json);
                if (cfg == null)
                {
                    return new AppConfig();
                }
                cfg.AdapterName = (cfg.AdapterName ?? "").Trim();
                cfg.IpAddress = (cfg.IpAddress ?? "").Trim();
                cfg.SubnetMask = string.IsNullOrWhiteSpace(cfg.SubnetMask) ? "255.255.255.0" : cfg.SubnetMask.Trim();
                cfg.Gateway = (cfg.Gateway ?? "").Trim();
                cfg.PrimaryDns = (cfg.PrimaryDns ?? "").Trim();
                cfg.SecondaryDns = (cfg.SecondaryDns ?? "").Trim();
                return cfg;
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(string path)
        {
            var normalized = new AppConfig
            {
                AdapterName = (AdapterName ?? "").Trim(),
                IpAddress = (IpAddress ?? "").Trim(),
                SubnetMask = string.IsNullOrWhiteSpace(SubnetMask) ? "255.255.255.0" : SubnetMask.Trim(),
                Gateway = (Gateway ?? "").Trim(),
                PrimaryDns = (PrimaryDns ?? "").Trim(),
                SecondaryDns = (SecondaryDns ?? "").Trim()
            };
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(normalized);
            File.WriteAllText(path, json, Encoding.UTF8);
        }
    }

    public class AdapterStatus
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsDhcpEnabled { get; set; }
        public string CurrentIp { get; set; }
        public string CurrentMask { get; set; }
        public string CurrentGateway { get; set; }
        public string CurrentDns { get; set; }
        public OperationalStatus Status { get; set; }

        public override string ToString()
        {
            string stateTag = Status == OperationalStatus.Up ? "已连接" : "未连接";
            return string.Format("{0} ({1} - {2})", Name, Description, stateTag);
        }
    }

    public static class NativeMethods
    {
        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanCloseHandle(
            IntPtr hClientHandle,
            IntPtr pReserved);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanQueryInterface(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            uint OpCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            out uint pWlanOpcodeValueType);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanGetProfileList(
            IntPtr hClientHandle,
            ref Guid pInterfaceGuid,
            IntPtr pReserved,
            out IntPtr ppProfileList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern void WlanFreeMemory(IntPtr pMemory);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanAllocateProfileIpConfiguration(
            uint dwIpAddressCount,
            uint dwGatewayCount,
            uint dwDnsServerCount,
            out uint pdwBufferSize,
            out IntPtr ppBuffer);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanVerifyProfileIpConfiguration(
            uint dwBufferSize,
            IntPtr pBuffer);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanProfileIpConfigurationGetAddressList(
            IntPtr pBuffer,
            out IntPtr ppAddressList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanProfileIpConfigurationGetGatewayList(
            IntPtr pBuffer,
            out IntPtr ppGatewayList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanProfileIpConfigurationGetDnsServerList(
            IntPtr pBuffer,
            out IntPtr ppDnsServerList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        public static extern uint WlanProfileIpConfigurationGetDnsServerConfigurationList(
            IntPtr pBuffer,
            out IntPtr ppDnsConfigList);

        [DllImport("wlanapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern uint WlanSetProfileMetadata(
            ref Guid pInterfaceGuid,
            [MarshalAs(UnmanagedType.LPWStr)] string strProfileName,
            uint dwFlags,
            [MarshalAs(UnmanagedType.LPWStr)] string strMetadataName,
            uint dwDataSize,
            IntPtr pData);

        [DllImport("wlanapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern uint WlanGetProfileMetadata(
            ref Guid pInterfaceGuid,
            [MarshalAs(UnmanagedType.LPWStr)] string strProfileName,
            uint dwFlags,
            [MarshalAs(UnmanagedType.LPWStr)] string strMetadataName,
            out uint pdwDataSize,
            out IntPtr ppData);

        [DllImport("dhcpcsvc.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern uint DhcpEnableDhcp(
            [MarshalAs(UnmanagedType.LPWStr)] string adapterGuidString,
            [MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("dhcpcsvc.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern uint DhcpAcquireParameters(
            [MarshalAs(UnmanagedType.LPWStr)] string adapterGuidString);

        [DllImport("dhcpcsvc.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern uint DhcpFallbackRefreshParams(
            [MarshalAs(UnmanagedType.LPWStr)] string adapterGuidString);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int ConvertInterfaceGuidToLuid(
            ref Guid interfaceGuid,
            out ulong interfaceLuid);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int GetUnicastIpAddressTable(
            ushort family,
            out IntPtr table);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int DeleteUnicastIpAddressEntry(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern void InitializeUnicastIpAddressEntry(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int CreateUnicastIpAddressEntry(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int GetIpForwardTable2(
            ushort family,
            out IntPtr table);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int DeleteIpForwardEntry2(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern void InitializeIpForwardEntry(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int CreateIpForwardEntry2(IntPtr row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern void FreeMibTable(IntPtr memory);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SetInterfaceDnsSettings(
            ref Guid interfaceGuid,
            IntPtr settings);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFileW(string lpFileName);
    }

    public static class AutoStartHelper
    {
        public const string RunRegKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string RunRegValueName = "Win11IpSwitcher";

        public static string BuildRunRegistryValue(string exePath)
        {
            return string.Format("\"{0}\" --tray", (exePath ?? "").Trim());
        }

        public static void TryRemoveZoneIdentifier(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;
            try
            {
                NativeMethods.DeleteFileW(filePath + ":Zone.Identifier");
            }
            catch
            {
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegKeyPath, false))
                {
                    if (key != null)
                    {
                        string val = key.GetValue(RunRegValueName) as string;
                        return !string.IsNullOrWhiteSpace(val);
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool SetAutoStart(bool enable, string exePath, out string message)
        {
            message = "";
            TryRemoveZoneIdentifier(exePath);

            if (enable)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegKeyPath, true))
                    {
                        if (key != null)
                        {
                            key.SetValue(RunRegValueName, BuildRunRegistryValue(exePath), RegistryValueKind.String);
                            message = "已开启开机自启动（登录时静默驻留系统托盘）。";
                            return true;
                        }
                    }
                    message = "无法打开当前用户启动项注册表。";
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                }
                return false;
            }
            else
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegKeyPath, true))
                    {
                        if (key != null && key.GetValue(RunRegValueName) != null)
                        {
                            key.DeleteValue(RunRegValueName, false);
                        }
                    }
                    message = "已关闭开机自动启动。";
                    return true;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    return false;
                }
            }
        }
    }

    public static class TrayIconFactory
    {
        public static Icon CreateModeIcon(bool isDhcp)
        {
            using (var bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                Color bg = isDhcp ? Color.FromArgb(25, 135, 84) : Color.FromArgb(13, 110, 253);
                using (var brush = new SolidBrush(bg))
                {
                    g.FillEllipse(brush, 1, 1, 29, 29);
                }
                using (var pen = new Pen(Color.White, 2f))
                {
                    g.DrawEllipse(pen, 2, 2, 27, 27);
                }

                string text = isDhcp ? "A" : "M";
                using (var font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(text, font, textBrush, new RectangleF(1, 2, 30, 29), sf);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }

    public static class NetworkHelper
    {
        public static bool IsValidIpv4(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;
            string trimmed = input.Trim();
            string[] parts = trimmed.Split('.');
            if (parts.Length != 4)
                return false;
            foreach (string part in parts)
            {
                if (part.Length == 0 || (part.Length > 1 && part.StartsWith("0")))
                    return false;
                int val;
                if (!int.TryParse(part, out val) || val < 0 || val > 255)
                    return false;
            }
            IPAddress addr;
            return IPAddress.TryParse(trimmed, out addr) && addr.AddressFamily == AddressFamily.InterNetwork;
        }

        public static int SubnetMaskToPrefixLength(string maskStr)
        {
            IPAddress maskAddr;
            if (!IPAddress.TryParse((maskStr ?? "").Trim(), out maskAddr) || maskAddr.AddressFamily != AddressFamily.InterNetwork)
                return 24;
            byte[] bytes = maskAddr.GetAddressBytes();
            int bits = 0;
            foreach (byte b in bytes)
            {
                for (int i = 7; i >= 0; i--)
                {
                    if (((b >> i) & 1) == 1)
                        bits++;
                    else
                        return bits;
                }
            }
            return bits;
        }

        public static string DecodeDhcpNetworkHint(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                return "";
            string h = hint.Trim();
            if (h.Length % 2 != 0)
                return "";
            try
            {
                byte[] bytes = new byte[h.Length / 2];
                for (int i = 0; i < h.Length; i += 2)
                {
                    string swapped = new string(new char[] { h[i + 1], h[i] });
                    bytes[i / 2] = byte.Parse(swapped, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }

        public static byte[] BuildWlanProfileIpConfigBlob(AppConfig staticCfg)
        {
            uint ipCount = staticCfg == null ? 0u : 1u;
            uint gwCount = (staticCfg != null && !string.IsNullOrWhiteSpace(staticCfg.Gateway)) ? 1u : 0u;
            var dnsList = new List<byte[]>();
            if (staticCfg != null)
            {
                if (!string.IsNullOrWhiteSpace(staticCfg.PrimaryDns))
                {
                    dnsList.Add(IPAddress.Parse(staticCfg.PrimaryDns.Trim()).GetAddressBytes());
                }
                if (!string.IsNullOrWhiteSpace(staticCfg.SecondaryDns))
                {
                    dnsList.Add(IPAddress.Parse(staticCfg.SecondaryDns.Trim()).GetAddressBytes());
                }
            }
            uint dnsCount = (uint)dnsList.Count;

            uint size;
            IntPtr ptr;
            uint res = NativeMethods.WlanAllocateProfileIpConfiguration(ipCount, gwCount, dnsCount, out size, out ptr);
            if (res != 0 || ptr == IntPtr.Zero)
                return null;

            try
            {
                if (staticCfg != null)
                {
                    IntPtr pAddr;
                    if (NativeMethods.WlanProfileIpConfigurationGetAddressList(ptr, out pAddr) == 0 && pAddr != IntPtr.Zero)
                    {
                        byte[] ipBytes = IPAddress.Parse(staticCfg.IpAddress.Trim()).GetAddressBytes();
                        Marshal.WriteInt32(pAddr, 0, 2); // AF_INET
                        Marshal.Copy(ipBytes, 0, new IntPtr(pAddr.ToInt64() + 4), 4);
                        Marshal.WriteInt32(pAddr, 128, SubnetMaskToPrefixLength(staticCfg.SubnetMask));
                    }

                    if (gwCount > 0)
                    {
                        IntPtr pGw;
                        if (NativeMethods.WlanProfileIpConfigurationGetGatewayList(ptr, out pGw) == 0 && pGw != IntPtr.Zero)
                        {
                            byte[] gwBytes = IPAddress.Parse(staticCfg.Gateway.Trim()).GetAddressBytes();
                            Marshal.WriteInt32(pGw, 0, 2); // AF_INET
                            Marshal.Copy(gwBytes, 0, new IntPtr(pGw.ToInt64() + 4), 4);
                        }
                    }

                    if (dnsCount > 0)
                    {
                        IntPtr pDns;
                        IntPtr pDnsCfg;
                        if (NativeMethods.WlanProfileIpConfigurationGetDnsServerList(ptr, out pDns) == 0 &&
                            NativeMethods.WlanProfileIpConfigurationGetDnsServerConfigurationList(ptr, out pDnsCfg) == 0 &&
                            pDns != IntPtr.Zero && pDnsCfg != IntPtr.Zero)
                        {
                            for (int i = 0; i < dnsList.Count; i++)
                            {
                                IntPtr entryPtr = new IntPtr(pDns.ToInt64() + i * 128);
                                Marshal.WriteInt32(entryPtr, 0, 2); // AF_INET
                                Marshal.Copy(dnsList[i], 0, new IntPtr(entryPtr.ToInt64() + 4), 4);

                                IntPtr cfgPtr = new IntPtr(pDnsCfg.ToInt64() + i * 4104);
                                Marshal.WriteInt32(cfgPtr, 0, 1);
                            }
                        }
                    }
                }

                uint verifyRes = NativeMethods.WlanVerifyProfileIpConfiguration(size, ptr);
                if (verifyRes != 0)
                    return null;

                byte[] managed = new byte[size];
                Marshal.Copy(ptr, managed, 0, (int)size);
                return managed;
            }
            finally
            {
                NativeMethods.WlanFreeMemory(ptr);
            }
        }

        public static bool ValidateStaticConfig(AppConfig cfg, out string errorMessage)
        {
            errorMessage = "";
            if (cfg == null)
            {
                errorMessage = "配置对象为空。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(cfg.AdapterName))
            {
                errorMessage = "请先选择目标网卡。";
                return false;
            }
            if (!IsValidIpv4(cfg.IpAddress))
            {
                errorMessage = "请输入合法的 IPv4 地址（例如 192.168.1.200）。";
                return false;
            }
            if (!IsValidIpv4(cfg.SubnetMask))
            {
                errorMessage = "请输入合法的子网掩码（例如 255.255.255.0）。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(cfg.Gateway) && !IsValidIpv4(cfg.Gateway))
            {
                errorMessage = "默认网关格式不正确（如不需要可留空）。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(cfg.PrimaryDns) && !IsValidIpv4(cfg.PrimaryDns))
            {
                errorMessage = "首选 DNS 格式不正确（如不需要可留空）。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(cfg.SecondaryDns))
            {
                if (string.IsNullOrWhiteSpace(cfg.PrimaryDns))
                {
                    errorMessage = "填写备用 DNS 前请先填写首选 DNS。";
                    return false;
                }
                if (!IsValidIpv4(cfg.SecondaryDns))
                {
                    errorMessage = "备用 DNS 格式不正确。";
                    return false;
                }
            }
            return true;
        }

        public static string[] BuildDhcpCommands(string adapterName)
        {
            string safeName = (adapterName ?? "").Trim().Replace("\"", "");
            return new string[]
            {
                string.Format("interface ipv4 set address name=\"{0}\" source=dhcp", safeName),
                string.Format("interface ipv4 set dnsservers name=\"{0}\" source=dhcp", safeName)
            };
        }

        public static string[] BuildStaticCommands(AppConfig cfg)
        {
            string safeName = (cfg.AdapterName ?? "").Trim().Replace("\"", "");
            string ip = cfg.IpAddress.Trim();
            string mask = cfg.SubnetMask.Trim();
            string gw = (cfg.Gateway ?? "").Trim();
            string dns1 = (cfg.PrimaryDns ?? "").Trim();
            string dns2 = (cfg.SecondaryDns ?? "").Trim();

            var cmds = new List<string>();
            if (!string.IsNullOrEmpty(gw))
            {
                cmds.Add(string.Format("interface ipv4 set address name=\"{0}\" static {1} {2} {3}", safeName, ip, mask, gw));
            }
            else
            {
                cmds.Add(string.Format("interface ipv4 set address name=\"{0}\" static {1} {2}", safeName, ip, mask));
            }

            if (!string.IsNullOrEmpty(dns1))
            {
                cmds.Add(string.Format("interface ipv4 set dnsservers name=\"{0}\" static {1} primary validate=no", safeName, dns1));
                if (!string.IsNullOrEmpty(dns2))
                {
                    cmds.Add(string.Format("interface ipv4 add dnsservers name=\"{0}\" {1} index=2 validate=no", safeName, dns2));
                }
            }
            else
            {
                cmds.Add(string.Format("interface ipv4 set dnsservers name=\"{0}\" source=dhcp", safeName));
            }

            return cmds.ToArray();
        }

        private static bool HasWlanStaticProfileMetadata(string adapterId, string hintProfileName)
        {
            try
            {
                string profilesRoot = @"SOFTWARE\Microsoft\WlanSvc\Interfaces\" + adapterId + @"\Profiles";
                using (RegistryKey profsKey = Registry.LocalMachine.OpenSubKey(profilesRoot, false))
                {
                    if (profsKey != null)
                    {
                        foreach (string subName in profsKey.GetSubKeyNames())
                        {
                            using (RegistryKey metaKey = Registry.LocalMachine.OpenSubKey(profilesRoot + @"\" + subName + @"\MetaData", false))
                            {
                                if (metaKey == null)
                                    continue;
                                byte[] existing = metaKey.GetValue("ProfileIpConfigurationV4") as byte[];
                                if (existing != null && existing.Length >= 8)
                                {
                                    int ipCount = BitConverter.ToInt32(existing, 4);
                                    if (ipCount > 0)
                                        return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static List<AdapterStatus> GetAdapters()
        {
            var result = new List<AdapterStatus>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                IPv4InterfaceProperties ipv4Props = null;
                try
                {
                    ipv4Props = ipProps.GetIPv4Properties();
                }
                catch
                {
                }

                bool isDhcp = ipv4Props != null && ipv4Props.IsDhcpEnabled;
                string regDns = "";
                string hintProfile = "";

                try
                {
                    string regPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + nic.Id;
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, false))
                    {
                        if (key != null)
                        {
                            object enableDhcpObj = key.GetValue("EnableDHCP");
                            object dhcpOffObj = key.GetValue("DhcpOffDueToStaticIp");
                            if (enableDhcpObj is int)
                            {
                                isDhcp = ((int)enableDhcpObj) != 0;
                            }
                            if (dhcpOffObj is int && ((int)dhcpOffObj) == 1)
                            {
                                isDhcp = false;
                            }
                            string hint = key.GetValue("DhcpNetworkHint") as string;
                            hintProfile = DecodeDhcpNetworkHint(hint);

                            string ns = key.GetValue("NameServer") as string;
                            string pns = key.GetValue("ProfileNameServer") as string;
                            if (!string.IsNullOrWhiteSpace(ns))
                                regDns = ns;
                            else if (!string.IsNullOrWhiteSpace(pns))
                                regDns = pns;
                        }
                    }
                }
                catch
                {
                }

                if (isDhcp && !string.IsNullOrWhiteSpace(hintProfile) && HasWlanStaticProfileMetadata(nic.Id, hintProfile))
                {
                    isDhcp = false;
                }

                string ip = "";
                string mask = "";

                foreach (UnicastIPAddressInformation uni in ipProps.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ip = uni.Address.ToString();
                        if (uni.IPv4Mask != null)
                        {
                            mask = uni.IPv4Mask.ToString();
                        }
                        break;
                    }
                }

                string gw = "";
                foreach (GatewayIPAddressInformation g in ipProps.GatewayAddresses)
                {
                    if (g.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().Equals("0.0.0.0"))
                    {
                        gw = g.Address.ToString();
                        break;
                    }
                }

                var dnsList = new List<string>();
                foreach (IPAddress d in ipProps.DnsAddresses)
                {
                    if (d.AddressFamily == AddressFamily.InterNetwork)
                    {
                        dnsList.Add(d.ToString());
                    }
                }
                if (dnsList.Count == 0 && !string.IsNullOrWhiteSpace(regDns))
                {
                    foreach (string s in regDns.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        dnsList.Add(s.Trim());
                    }
                }

                result.Add(new AdapterStatus
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    IsDhcpEnabled = isDhcp,
                    CurrentIp = ip,
                    CurrentMask = mask,
                    CurrentGateway = gw,
                    CurrentDns = string.Join(", ", dnsList.ToArray()),
                    Status = nic.OperationalStatus
                });
            }

            return SortAdaptersByPriority(result);
        }

        public static bool IsLikelyVirtualAdapter(string name, string description)
        {
            string combined = ((name ?? "") + " " + (description ?? "")).ToLowerInvariant();
            string[] virtualKeywords = new string[]
            {
                "virtual", "vmware", "vmnet", "vbox", "hyper-v", "vethernet", "wsl",
                "loopback", "tap-", "wintun", "tun ", "clash", "sing-box", "mihomo",
                "tailscale", "zerotier", "wireguard", "nordlynx", "radmin", "hamachi",
                "bluetooth", "npcap", "miniport", "teredo", "isatap"
            };
            foreach (string kw in virtualKeywords)
            {
                if (combined.Contains(kw))
                    return true;
            }
            return false;
        }

        public static List<AdapterStatus> SortAdaptersByPriority(IEnumerable<AdapterStatus> adapters)
        {
            if (adapters == null)
                return new List<AdapterStatus>();

            return adapters
                .OrderByDescending(a => a.Status == OperationalStatus.Up)
                .ThenByDescending(a => !IsLikelyVirtualAdapter(a.Name, a.Description))
                .ThenByDescending(a => !string.IsNullOrWhiteSpace(a.CurrentGateway))
                .ThenByDescending(a => !string.IsNullOrWhiteSpace(a.CurrentIp) && !a.CurrentIp.StartsWith("169.254."))
                .ThenBy(a => a.Name)
                .ToList();
        }

        public static int ChooseBestAdapterIndex(IList<AdapterStatus> sortedAdapters, string preferredName, bool requireUpForPreferred)
        {
            if (sortedAdapters == null || sortedAdapters.Count == 0)
                return -1;

            int preferredIdx = -1;
            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                for (int i = 0; i < sortedAdapters.Count; i++)
                {
                    if (string.Equals(sortedAdapters[i].Name, preferredName, StringComparison.OrdinalIgnoreCase))
                    {
                        preferredIdx = i;
                        break;
                    }
                }
            }

            if (preferredIdx >= 0)
            {
                if (!requireUpForPreferred || sortedAdapters[preferredIdx].Status == OperationalStatus.Up)
                {
                    return preferredIdx;
                }

                // Preferred adapter from config is currently disconnected; if any adapter is Up, pick the highest-priority Up adapter
                bool anyUp = sortedAdapters.Any(a => a.Status == OperationalStatus.Up);
                if (anyUp)
                {
                    return 0;
                }

                return preferredIdx;
            }

            return 0;
        }

        private static List<string> GetTargetWlanProfiles(Guid interfaceGuid, string adapterId, bool includeAllProfilesWithStaticMetadata)
        {
            var profiles = new List<string>();

            // 1. Check active connection via WlanQueryInterface (opcode 7 = wlan_intf_opcode_current_connection)
            IntPtr hClient = IntPtr.Zero;
            try
            {
                uint negVer;
                if (NativeMethods.WlanOpenHandle(2, IntPtr.Zero, out negVer, out hClient) == 0 && hClient != IntPtr.Zero)
                {
                    uint dataSize;
                    IntPtr ppData;
                    uint opcodeType;
                    if (NativeMethods.WlanQueryInterface(hClient, ref interfaceGuid, 7, IntPtr.Zero, out dataSize, out ppData, out opcodeType) == 0 &&
                        ppData != IntPtr.Zero)
                    {
                        try
                        {
                            string activeProfile = Marshal.PtrToStringUni(new IntPtr(ppData.ToInt64() + 8));
                            if (!string.IsNullOrWhiteSpace(activeProfile) && !profiles.Contains(activeProfile))
                            {
                                profiles.Add(activeProfile);
                            }
                        }
                        finally
                        {
                            NativeMethods.WlanFreeMemory(ppData);
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (hClient != IntPtr.Zero)
                {
                    NativeMethods.WlanCloseHandle(hClient, IntPtr.Zero);
                }
            }

            // 2. Also check DhcpNetworkHint from Tcpip interface registry and WlanSvc Profiles
            try
            {
                string regPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + adapterId;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, false))
                {
                    if (key != null)
                    {
                        string hint = key.GetValue("DhcpNetworkHint") as string;
                        string decoded = DecodeDhcpNetworkHint(hint);
                        if (!string.IsNullOrWhiteSpace(decoded) && !profiles.Contains(decoded))
                        {
                            profiles.Add(decoded);
                        }
                    }
                }
            }
            catch
            {
            }

            if (includeAllProfilesWithStaticMetadata)
            {
                try
                {
                    string profilesRoot = @"SOFTWARE\Microsoft\WlanSvc\Interfaces\" + adapterId + @"\Profiles";
                    using (RegistryKey profsKey = Registry.LocalMachine.OpenSubKey(profilesRoot, false))
                    {
                        if (profsKey != null)
                        {
                            foreach (string subName in profsKey.GetSubKeyNames())
                            {
                                using (RegistryKey profKey = Registry.LocalMachine.OpenSubKey(profilesRoot + @"\" + subName, false))
                                {
                                    if (profKey == null)
                                        continue;
                                    string profName = profKey.GetValue("ProfileName") as string;
                                    if (!string.IsNullOrWhiteSpace(profName) && !profiles.Contains(profName))
                                    {
                                        using (RegistryKey metaKey = profKey.OpenSubKey("MetaData", false))
                                        {
                                            if (metaKey != null)
                                            {
                                                byte[] existing = metaKey.GetValue("ProfileIpConfigurationV4") as byte[];
                                                if (existing != null && existing.Length > 28)
                                                {
                                                    profiles.Add(profName);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return profiles;
        }

        private static void AppendStepLog(StringBuilder log, string message)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, message);
            log.AppendLine(line);
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_run.log");
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool UpdateWlanProfileIpMetadata(Guid interfaceGuid, string adapterId, AppConfig staticCfg, StringBuilder log)
        {
            AppendStepLog(log, "Building WlanProfileIpConfigBlob (isStatic=" + (staticCfg != null) + ")");
            byte[] blob = BuildWlanProfileIpConfigBlob(staticCfg);
            if (blob == null)
            {
                AppendStepLog(log, "BuildWlanProfileIpConfigBlob returned null");
                return false;
            }
            AppendStepLog(log, "Built WlanProfileIpConfigBlob of " + blob.Length + " bytes");

            List<string> profileNames = GetTargetWlanProfiles(interfaceGuid, adapterId, staticCfg == null);
            AppendStepLog(log, "Target WLAN profiles: " + string.Join(", ", profileNames.ToArray()));
            bool anySuccess = false;

            // 1. Notify WlanSvc via WlanSetProfileMetadata API for the target profile(s)
            IntPtr pBlob = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, pBlob, blob.Length);
                foreach (string profileName in profileNames)
                {
                    AppendStepLog(log, "Calling WlanSetProfileMetadata for " + profileName);
                    uint res = NativeMethods.WlanSetProfileMetadata(
                        ref interfaceGuid,
                        profileName,
                        0,
                        "ProfileIpConfigurationV4",
                        (uint)blob.Length,
                        pBlob);
                    AppendStepLog(log, string.Format("WlanSetProfileMetadata({0}) -> {1}", profileName, res));
                    if (res == 0)
                    {
                        anySuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendStepLog(log, "WlanSetProfileMetadata error: " + ex.Message);
            }
            finally
            {
                Marshal.FreeHGlobal(pBlob);
            }

            // 2. When switching to DHCP, also ensure any stale >28 byte ProfileIpConfigurationV4 in registry is cleared
            if (staticCfg == null)
            {
                try
                {
                    string profilesRoot = @"SOFTWARE\Microsoft\WlanSvc\Interfaces\" + adapterId + @"\Profiles";
                    using (RegistryKey profsKey = Registry.LocalMachine.OpenSubKey(profilesRoot, false))
                    {
                        if (profsKey != null)
                        {
                            foreach (string subName in profsKey.GetSubKeyNames())
                            {
                                using (RegistryKey metaKey = Registry.LocalMachine.OpenSubKey(profilesRoot + @"\" + subName + @"\MetaData", true))
                                {
                                    if (metaKey == null)
                                        continue;
                                    byte[] existing = metaKey.GetValue("ProfileIpConfigurationV4") as byte[];
                                    if (existing != null && existing.Length > 28)
                                    {
                                        metaKey.SetValue("ProfileIpConfigurationV4", blob, RegistryValueKind.Binary);
                                        AppendStepLog(log, "Cleared registry ProfileIpConfigurationV4 on profile " + subName);
                                        anySuccess = true;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendStepLog(log, "Registry WlanSvc MetaData sync note: " + ex.Message);
                }
            }

            return anySuccess;
        }

        private static void ClearAdapterLevelStaticRegistryKeys(string adapterId, StringBuilder log)
        {
            try
            {
                string regPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + adapterId;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue("IPAddress", new string[0], RegistryValueKind.MultiString);
                        key.SetValue("SubnetMask", new string[0], RegistryValueKind.MultiString);
                        key.SetValue("DefaultGateway", new string[0], RegistryValueKind.MultiString);
                        key.SetValue("DefaultGatewayMetric", new string[0], RegistryValueKind.MultiString);
                        key.SetValue("NameServer", "", RegistryValueKind.String);
                        AppendStepLog(log, "Cleared adapter-level static IPAddress/SubnetMask/DefaultGateway/NameServer in Tcpip registry");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendStepLog(log, "ClearAdapterLevelStaticRegistryKeys note: " + ex.Message);
            }
        }

        private static void ApplyInterfaceDnsSettings(Guid interfaceGuid, string adapterId, string dns1, string dns2, bool isWlanProfile, StringBuilder log)
        {
            var dnsParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dns1)) dnsParts.Add(dns1.Trim());
            if (!string.IsNullOrWhiteSpace(dns2)) dnsParts.Add(dns2.Trim());
            string joinedDns = dnsParts.Count > 0 ? string.Join(",", dnsParts.ToArray()) : null;

            // 1. Call iphlpapi.dll!SetInterfaceDnsSettings(ref Guid, ref DNS_INTERFACE_SETTINGS3)
            IntPtr pSettings = Marshal.AllocHGlobal(112);
            IntPtr pNameServer = IntPtr.Zero;
            try
            {
                for (int i = 0; i < 112; i++) Marshal.WriteByte(pSettings, i, 0);
                Marshal.WriteInt32(pSettings, 0, 3); // DNS_INTERFACE_SETTINGS_VERSION3
                long flags = isWlanProfile ? 0x1002L : 0x0002L; // 0x1002 = DNS_SETTING_NAMESERVER | DNS_SETTING_PROFILE
                Marshal.WriteInt64(pSettings, 8, flags);
                if (!string.IsNullOrEmpty(joinedDns))
                {
                    pNameServer = Marshal.StringToHGlobalUni(joinedDns);
                    Marshal.WriteIntPtr(pSettings, 24, pNameServer);
                }
                AppendStepLog(log, "Calling SetInterfaceDnsSettings (flags=0x" + flags.ToString("X") + ")");
                int res = NativeMethods.SetInterfaceDnsSettings(ref interfaceGuid, pSettings);
                AppendStepLog(log, "SetInterfaceDnsSettings -> " + res);
            }
            catch (Exception ex)
            {
                AppendStepLog(log, "SetInterfaceDnsSettings error: " + ex.Message);
            }
            finally
            {
                if (pNameServer != IntPtr.Zero) Marshal.FreeHGlobal(pNameServer);
                Marshal.FreeHGlobal(pSettings);
            }

            // 2. Ensure NameServer and ProfileNameServer in Tcpip registry match Windows 11 native behavior
            try
            {
                string regPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + adapterId;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, true))
                {
                    if (key != null)
                    {
                        if (isWlanProfile)
                        {
                            // For WLAN profiles, Windows 11 stores per-profile DNS in ProfileNameServer and leaves adapter-level NameServer empty
                            key.SetValue("NameServer", "", RegistryValueKind.String);
                            key.SetValue("ProfileNameServer", joinedDns ?? "", RegistryValueKind.String);
                        }
                        else
                        {
                            key.SetValue("NameServer", joinedDns ?? "", RegistryValueKind.String);
                            if (key.GetValue("ProfileNameServer") != null && string.IsNullOrEmpty(joinedDns))
                            {
                                key.SetValue("ProfileNameServer", "", RegistryValueKind.String);
                            }
                        }
                        AppendStepLog(log, "Synchronized Tcpip registry DNS (isWlanProfile=" + isWlanProfile + ", dns='" + (joinedDns ?? "") + "')");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendStepLog(log, "Registry DNS sync note: " + ex.Message);
            }
        }

        private static void DeleteManualIpAndGatewayEntries(Guid interfaceGuid, StringBuilder log)
        {
            ulong targetLuid;
            if (NativeMethods.ConvertInterfaceGuidToLuid(ref interfaceGuid, out targetLuid) != 0)
                return;

            IntPtr pUniTable = IntPtr.Zero;
            if (NativeMethods.GetUnicastIpAddressTable(2, out pUniTable) == 0 && pUniTable != IntPtr.Zero)
            {
                try
                {
                    int count = Marshal.ReadInt32(pUniTable, 0);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr rowPtr = new IntPtr(pUniTable.ToInt64() + 8 + i * 80);
                        ulong rowLuid = (ulong)Marshal.ReadInt64(rowPtr, 32);
                        int prefixOrigin = Marshal.ReadInt32(rowPtr, 44);
                        int suffixOrigin = Marshal.ReadInt32(rowPtr, 48);
                        if (rowLuid == targetLuid && (prefixOrigin == 1 || suffixOrigin == 1))
                        {
                            int delRes = NativeMethods.DeleteUnicastIpAddressEntry(rowPtr);
                            AppendStepLog(log, "DeleteUnicastIpAddressEntry -> " + delRes);
                        }
                    }
                }
                finally
                {
                    NativeMethods.FreeMibTable(pUniTable);
                }
            }

            IntPtr pRouteTable = IntPtr.Zero;
            if (NativeMethods.GetIpForwardTable2(2, out pRouteTable) == 0 && pRouteTable != IntPtr.Zero)
            {
                try
                {
                    int count = Marshal.ReadInt32(pRouteTable, 0);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr rowPtr = new IntPtr(pRouteTable.ToInt64() + 8 + i * 104);
                        ulong rowLuid = (ulong)Marshal.ReadInt64(rowPtr, 0);
                        byte destPrefixLen = Marshal.ReadByte(rowPtr, 40);
                        int protocol = Marshal.ReadInt32(rowPtr, 88);
                        if (rowLuid == targetLuid && destPrefixLen == 0 && protocol == 3)
                        {
                            int delRes = NativeMethods.DeleteIpForwardEntry2(rowPtr);
                            AppendStepLog(log, "DeleteIpForwardEntry2 -> " + delRes);
                        }
                    }
                }
                finally
                {
                    NativeMethods.FreeMibTable(pRouteTable);
                }
            }
        }

        private static void CreateManualIpAndGatewayEntries(Guid interfaceGuid, AppConfig cfg, StringBuilder log)
        {
            ulong targetLuid;
            if (NativeMethods.ConvertInterfaceGuidToLuid(ref interfaceGuid, out targetLuid) != 0)
                return;

            IntPtr uniRow = Marshal.AllocHGlobal(80);
            try
            {
                for (int i = 0; i < 80; i++) Marshal.WriteByte(uniRow, i, 0);
                NativeMethods.InitializeUnicastIpAddressEntry(uniRow);
                Marshal.WriteInt16(uniRow, 0, 2); // AF_INET
                byte[] ipBytes = IPAddress.Parse(cfg.IpAddress.Trim()).GetAddressBytes();
                Marshal.Copy(ipBytes, 0, new IntPtr(uniRow.ToInt64() + 4), 4);
                Marshal.WriteInt64(uniRow, 32, (long)targetLuid);
                Marshal.WriteInt32(uniRow, 44, 1); // IpPrefixOriginManual
                Marshal.WriteInt32(uniRow, 48, 1); // IpSuffixOriginManual
                Marshal.WriteByte(uniRow, 60, (byte)SubnetMaskToPrefixLength(cfg.SubnetMask));
                Marshal.WriteInt32(uniRow, 64, 4); // IpDadStatePreferred
                int createIpRes = NativeMethods.CreateUnicastIpAddressEntry(uniRow);
                AppendStepLog(log, "CreateUnicastIpAddressEntry -> " + createIpRes);
            }
            finally
            {
                Marshal.FreeHGlobal(uniRow);
            }

            if (!string.IsNullOrWhiteSpace(cfg.Gateway))
            {
                IntPtr routeRow = Marshal.AllocHGlobal(104);
                try
                {
                    for (int i = 0; i < 104; i++) Marshal.WriteByte(routeRow, i, 0);
                    NativeMethods.InitializeIpForwardEntry(routeRow);
                    Marshal.WriteInt64(routeRow, 0, (long)targetLuid);
                    Marshal.WriteInt16(routeRow, 12, 2); // DestinationPrefix.Prefix.si_family = AF_INET
                    Marshal.WriteByte(routeRow, 40, 0);  // DestinationPrefix.PrefixLength = 0
                    Marshal.WriteInt16(routeRow, 44, 2); // NextHop.si_family = AF_INET
                    byte[] gwBytes = IPAddress.Parse(cfg.Gateway.Trim()).GetAddressBytes();
                    Marshal.Copy(gwBytes, 0, new IntPtr(routeRow.ToInt64() + 48), 4);
                    Marshal.WriteInt32(routeRow, 84, 1); // Metric = 1
                    Marshal.WriteInt32(routeRow, 88, 3); // MIB_IPPROTO_NETMGMT
                    int createGwRes = NativeMethods.CreateIpForwardEntry2(routeRow);
                    AppendStepLog(log, "CreateIpForwardEntry2 -> " + createGwRes);
                }
                finally
                {
                    Marshal.FreeHGlobal(routeRow);
                }
            }
        }

        public static bool ApplyDhcpConfiguration(string adapterName, out string diagnosticOutput)
        {
            var log = new StringBuilder();
            AppendStepLog(log, "=== ApplyDhcpConfiguration start for '" + adapterName + "' ===");
            NetworkInterface targetNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, adapterName, StringComparison.OrdinalIgnoreCase));

            bool handledViaWlanProfile = false;
            if (targetNic != null)
            {
                string adapterId = targetNic.Id;
                Guid interfaceGuid;
                if (Guid.TryParse(adapterId, out interfaceGuid))
                {
                    // 1. Update Windows 11 WlanSvc ProfileIpConfigurationV4 metadata to Automatic (DHCP)
                    handledViaWlanProfile = UpdateWlanProfileIpMetadata(interfaceGuid, adapterId, null, log);

                    // 2. Clear static DNS and adapter-level static registry values BEFORE enabling DHCP
                    // so we never overwrite Tcpip registry keys after DhcpEnableDhcp(true) acquires the lease
                    ClearAdapterLevelStaticRegistryKeys(adapterId, log);
                    ApplyInterfaceDnsSettings(interfaceGuid, adapterId, null, null, handledViaWlanProfile, log);

                    // 3. Ensure DHCP is in disabled state while removing manual IP and manual gateway route,
                    // preventing DeleteManualIpAndGatewayEntries from deleting a newly-acquired DHCP lease
                    // when the Static IP happens to be identical to the DHCP-assigned IP
                    AppendStepLog(log, "Calling DhcpEnableDhcp(false) before deleting manual IP/route");
                    uint dhcpOffRes = NativeMethods.DhcpEnableDhcp(adapterId, false);
                    AppendStepLog(log, "DhcpEnableDhcp(false) -> " + dhcpOffRes);

                    AppendStepLog(log, "Calling DeleteManualIpAndGatewayEntries");
                    DeleteManualIpAndGatewayEntries(interfaceGuid, log);

                    // 4. Transition DHCP from disabled (0) -> enabled (1) on the clean interface and acquire lease parameters
                    AppendStepLog(log, "Calling DhcpEnableDhcp(true)");
                    uint dhcpOnRes = NativeMethods.DhcpEnableDhcp(adapterId, true);
                    AppendStepLog(log, "DhcpEnableDhcp(true) -> " + dhcpOnRes);

                    try
                    {
                        uint acqRes = NativeMethods.DhcpAcquireParameters(adapterId);
                        AppendStepLog(log, "DhcpAcquireParameters -> " + acqRes);
                        uint fbRes = NativeMethods.DhcpFallbackRefreshParams(adapterId);
                        AppendStepLog(log, "DhcpFallbackRefreshParams -> " + fbRes);
                    }
                    catch (Exception ex)
                    {
                        AppendStepLog(log, "DhcpAcquireParameters note: " + ex.Message);
                    }
                }
            }

            // 5. Only run netsh DHCP commands for non-WLAN adapters
            if (!handledViaWlanProfile)
            {
                AppendStepLog(log, "Running netsh DHCP commands (non-WLAN adapter)");
                string netshOut;
                ExecuteNetshCommands(BuildDhcpCommands(adapterName), out netshOut);
                if (!string.IsNullOrWhiteSpace(netshOut))
                {
                    AppendStepLog(log, "netsh output: " + netshOut);
                }
            }

            // 6. Poll briefly until adapter reports DHCP enabled, valid IP (not 169.254.x.x), and Default Gateway
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(350);
                AdapterStatus current = GetAdapters().FirstOrDefault(a => string.Equals(a.Name, adapterName, StringComparison.OrdinalIgnoreCase));
                if (current != null &&
                    current.IsDhcpEnabled &&
                    !string.IsNullOrWhiteSpace(current.CurrentIp) &&
                    !current.CurrentIp.StartsWith("169.254.") &&
                    !string.IsNullOrWhiteSpace(current.CurrentGateway))
                {
                    AppendStepLog(log, string.Format("=== ApplyDhcpConfiguration succeeded (IP={0}, GW={1}, DNS={2}) ===", current.CurrentIp, current.CurrentGateway, current.CurrentDns));
                    diagnosticOutput = log.ToString().Trim();
                    return true;
                }
                if (i == 3 && targetNic != null)
                {
                    try
                    {
                        NativeMethods.DhcpAcquireParameters(targetNic.Id);
                    }
                    catch
                    {
                    }
                }
            }

            AdapterStatus finalState = GetAdapters().FirstOrDefault(a => string.Equals(a.Name, adapterName, StringComparison.OrdinalIgnoreCase));
            AppendStepLog(log, string.Format("=== ApplyDhcpConfiguration finished (IsDhcp={0}, IP={1}, GW={2}) ===",
                finalState != null && finalState.IsDhcpEnabled,
                finalState != null ? finalState.CurrentIp : "",
                finalState != null ? finalState.CurrentGateway : ""));
            diagnosticOutput = log.ToString().Trim();
            return finalState != null && finalState.IsDhcpEnabled;
        }

        public static bool ApplyStaticConfiguration(AppConfig cfg, out string diagnosticOutput)
        {
            var log = new StringBuilder();
            AppendStepLog(log, "=== ApplyStaticConfiguration start for '" + cfg.AdapterName + "' (" + cfg.IpAddress + ") ===");
            NetworkInterface targetNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, cfg.AdapterName, StringComparison.OrdinalIgnoreCase));

            bool handledViaWlanProfile = false;
            if (targetNic != null)
            {
                string adapterId = targetNic.Id;
                Guid interfaceGuid;
                if (Guid.TryParse(adapterId, out interfaceGuid))
                {
                    // 1. Update Windows 11 WlanSvc ProfileIpConfigurationV4 metadata to Manual (Static IP)
                    handledViaWlanProfile = UpdateWlanProfileIpMetadata(interfaceGuid, adapterId, cfg, log);

                    // 2. If a previous run left adapter-level static IP keys via netsh, clear them so Win11 Settings won't show the yellow warning
                    if (handledViaWlanProfile)
                    {
                        ClearAdapterLevelStaticRegistryKeys(adapterId, log);
                    }

                    // 3. Disable DHCP via dhcpcsvc, replace manual IP & Gateway entries via iphlpapi (exact WlanMediaManager sequence)
                    AppendStepLog(log, "Calling DhcpEnableDhcp(false) #1");
                    uint dhcpRes1 = NativeMethods.DhcpEnableDhcp(adapterId, false);
                    AppendStepLog(log, "DhcpEnableDhcp(false) #1 -> " + dhcpRes1);

                    AppendStepLog(log, "Calling DeleteManualIpAndGatewayEntries");
                    DeleteManualIpAndGatewayEntries(interfaceGuid, log);
                    AppendStepLog(log, "Calling CreateManualIpAndGatewayEntries");
                    CreateManualIpAndGatewayEntries(interfaceGuid, cfg, log);

                    AppendStepLog(log, "Calling DhcpEnableDhcp(false) #2");
                    uint dhcpRes2 = NativeMethods.DhcpEnableDhcp(adapterId, false);
                    AppendStepLog(log, "DhcpEnableDhcp(false) #2 -> " + dhcpRes2);

                    // 4. Apply DNS settings (ProfileNameServer for WLAN, NameServer for wired adapters) via SetInterfaceDnsSettings
                    ApplyInterfaceDnsSettings(interfaceGuid, adapterId, cfg.PrimaryDns, cfg.SecondaryDns, handledViaWlanProfile, log);
                }
            }

            // 5. Only run netsh static IP/DNS commands for non-WLAN (wired) adapters where WlanSvc profiles do not apply
            if (!handledViaWlanProfile)
            {
                AppendStepLog(log, "Running netsh Static commands (non-WLAN adapter)");
                string netshOut;
                ExecuteNetshCommands(BuildStaticCommands(cfg), out netshOut);
                if (!string.IsNullOrWhiteSpace(netshOut))
                {
                    AppendStepLog(log, "netsh output: " + netshOut);
                }
            }

            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(300);
                AdapterStatus current = GetAdapters().FirstOrDefault(a => string.Equals(a.Name, cfg.AdapterName, StringComparison.OrdinalIgnoreCase));
                if (current != null && !current.IsDhcpEnabled && string.Equals(current.CurrentIp, cfg.IpAddress.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    AppendStepLog(log, "=== ApplyStaticConfiguration succeeded (IP=" + current.CurrentIp + ") ===");
                    diagnosticOutput = log.ToString().Trim();
                    return true;
                }
            }

            AdapterStatus finalState = GetAdapters().FirstOrDefault(a => string.Equals(a.Name, cfg.AdapterName, StringComparison.OrdinalIgnoreCase));
            AppendStepLog(log, "=== ApplyStaticConfiguration finished (IsDhcp=" + (finalState != null && finalState.IsDhcpEnabled) + ") ===");
            diagnosticOutput = log.ToString().Trim();
            return finalState != null && !finalState.IsDhcpEnabled;
        }

        public static bool ExecuteNetshCommands(string[] argsList, out string output)
        {
            var sb = new StringBuilder();
            foreach (string args in argsList)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    string stdOut = p.StandardOutput.ReadToEnd();
                    string stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrWhiteSpace(stdOut))
                        sb.AppendLine(stdOut.Trim());
                    if (!string.IsNullOrWhiteSpace(stdErr))
                        sb.AppendLine(stdErr.Trim());

                    if (p.ExitCode != 0)
                    {
                        string combined = (stdOut + " " + stdErr).ToLowerInvariant();
                        bool alreadyDhcp = args.Contains("source=dhcp") &&
                            (combined.Contains("已经在此接口上启用") || combined.Contains("已经启用") || combined.Contains("already enabled on this interface"));
                        if (!alreadyDhcp)
                        {
                            output = sb.ToString().Trim();
                            return false;
                        }
                    }
                }
            }
            output = sb.ToString().Trim();
            return true;
        }

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public class MainForm : Form
    {
        private readonly string _configPath;
        private readonly bool _startInTray;
        private AppConfig _config;
        private List<AdapterStatus> _adapters = new List<AdapterStatus>();

        private ComboBox _cmbAdapters;
        private Button _btnRefresh;
        private Label _lblModeBadge;
        private Label _lblCurrentDetails;
        private Button _btnCopyCurrent;

        private TextBox _txtIp;
        private TextBox _txtMask;
        private TextBox _txtGateway;
        private TextBox _txtDns1;
        private TextBox _txtDns2;
        private Button _btnSaveOnly;

        private CheckBox _chkAutoStart;
        private bool _suppressAutoStartEvent;

        private Button _btnSwitchDhcp;
        private Button _btnSwitchStatic;
        private Label _lblStatusMessage;

        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _menuItemStatus;
        private ToolStripMenuItem _menuItemDhcp;
        private ToolStripMenuItem _menuItemStatic;
        private ToolStripMenuItem _menuItemAutoStart;
        private ToolStripMenuItem _menuItemOpen;
        private ToolStripMenuItem _menuItemQuit;
        private Icon _iconDhcp;
        private Icon _iconStatic;
        private bool _allowExit;
        private bool _hasShownTrayBalloon;
        private EventWaitHandle _showWindowEvent;
        private Thread _ipcThread;
        private readonly string _initialAction;

        public MainForm(string configPath, bool startInTray = false, EventWaitHandle showWindowEvent = null, string initialAction = null)
        {
            _configPath = configPath;
            _startInTray = startInTray;
            _showWindowEvent = showWindowEvent;
            _initialAction = initialAction;
            _config = AppConfig.Load(_configPath);
            InitializeLayout();
            InitializeTray();
            LoadAdaptersAndConfig();
            StartSingleInstanceListener();

            if (!string.IsNullOrWhiteSpace(_initialAction) && IsHandleCreated)
            {
                ScheduleInitialAction();
            }
            else if (!string.IsNullOrWhiteSpace(_initialAction))
            {
                HandleCreated += (s, e) => ScheduleInitialAction();
            }
        }

        private void ScheduleInitialAction()
        {
            BeginInvoke(new Action(() =>
            {
                if (_initialAction == "--do-dhcp")
                {
                    OnSwitchToDhcpClicked(_startInTray);
                }
                else if (_initialAction == "--do-static")
                {
                    OnSwitchToStaticClicked(_startInTray);
                }
            }));
        }

        public bool StartInTray
        {
            get { return _startInTray; }
        }

        public NotifyIcon TrayNotifyIcon
        {
            get { return _notifyIcon; }
        }

        public CheckBox AutoStartCheckBox
        {
            get { return _chkAutoStart; }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startInTray && !IsHandleCreated && value)
            {
                ShowInTaskbar = false;
                base.SetVisibleCore(false);
                var handle = Handle; // Ensure HWND is created so IPC Invoke works while hidden
                return;
            }
            base.SetVisibleCore(value);
        }

        private void StartSingleInstanceListener()
        {
            if (_showWindowEvent == null)
                return;

            _ipcThread = new Thread(() =>
            {
                while (!_allowExit)
                {
                    try
                    {
                        if (_showWindowEvent.WaitOne(500))
                        {
                            if (_allowExit) break;
                            if (IsHandleCreated)
                            {
                                BeginInvoke(new Action(RestoreFromTray));
                            }
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            });
            _ipcThread.IsBackground = true;
            _ipcThread.Start();
        }

        public string SelectedAdapterName
        {
            get
            {
                var item = _cmbAdapters.SelectedItem as AdapterStatus;
                return item != null ? item.Name : (_config.AdapterName ?? "");
            }
        }

        public AppConfig CollectFormConfig()
        {
            return new AppConfig
            {
                AdapterName = SelectedAdapterName,
                IpAddress = _txtIp.Text.Trim(),
                SubnetMask = _txtMask.Text.Trim(),
                Gateway = _txtGateway.Text.Trim(),
                PrimaryDns = _txtDns1.Text.Trim(),
                SecondaryDns = _txtDns2.Text.Trim()
            };
        }

        public void SetFormFields(string ip, string mask, string gw, string dns1, string dns2)
        {
            _txtIp.Text = ip ?? "";
            _txtMask.Text = string.IsNullOrWhiteSpace(mask) ? "255.255.255.0" : mask;
            _txtGateway.Text = gw ?? "";
            _txtDns1.Text = dns1 ?? "";
            _txtDns2.Text = dns2 ?? "";
        }

        public void SaveCurrentFormToConfig()
        {
            _config = CollectFormConfig();
            _config.Save(_configPath);
        }

        private void InitializeLayout()
        {
            bool isAdmin = NetworkHelper.IsAdministrator();
            Text = "Windows 11 网络 DHCP / 静态 IP 一键切换" + (isAdmin ? " (管理员)" : " (未提权 - 仅预览)");
            Size = new Size(520, 555);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            BackColor = Color.FromArgb(248, 249, 250);

            int padX = 20;
            int contentWidth = 465;

            // 1. Adapter selection row
            var lblAdapter = new Label
            {
                Text = "目标网卡：",
                Location = new Point(padX, 18),
                Size = new Size(75, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblAdapter);

            _cmbAdapters = new ComboBox
            {
                Location = new Point(padX + 78, 15),
                Size = new Size(305, 28),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAdapters.SelectedIndexChanged += (s, e) => UpdateCurrentAdapterStatusView();
            Controls.Add(_cmbAdapters);

            _btnRefresh = new Button
            {
                Text = "刷新",
                Location = new Point(padX + 390, 14),
                Size = new Size(75, 28),
                FlatStyle = FlatStyle.System
            };
            _btnRefresh.Click += (s, e) =>
            {
                RefreshAdapterList();
                SetStatusMessage("已刷新网卡实时状态。", Color.FromArgb(40, 90, 160));
            };
            Controls.Add(_btnRefresh);

            // 2. Current status card
            var grpCurrent = new GroupBox
            {
                Text = "当前实时网络状态",
                Location = new Point(padX, 52),
                Size = new Size(contentWidth, 118)
            };

            _lblModeBadge = new Label
            {
                Text = "● 正在读取状态...",
                Location = new Point(15, 24),
                Size = new Size(300, 22),
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
            };
            grpCurrent.Controls.Add(_lblModeBadge);

            _btnCopyCurrent = new Button
            {
                Text = "填入下方表单",
                Location = new Point(345, 20),
                Size = new Size(105, 26),
                FlatStyle = FlatStyle.System
            };
            _btnCopyCurrent.Click += (s, e) => CopyCurrentAdapterToForm();
            grpCurrent.Controls.Add(_btnCopyCurrent);

            _lblCurrentDetails = new Label
            {
                Text = "",
                Location = new Point(15, 52),
                Size = new Size(435, 56),
                ForeColor = Color.FromArgb(60, 64, 67)
            };
            grpCurrent.Controls.Add(_lblCurrentDetails);
            Controls.Add(grpCurrent);

            // 3. Static IP preset group
            var grpStatic = new GroupBox
            {
                Text = "手动配置参数（自动保存，下次打开直接使用）",
                Location = new Point(padX, 180),
                Size = new Size(contentWidth, 215)
            };

            _txtIp = AddLabeledTextBox(grpStatic, "IPv4 地址：", 30, "例如 192.168.1.200");
            _txtMask = AddLabeledTextBox(grpStatic, "子网掩码：", 62, "默认 255.255.255.0");
            _txtGateway = AddLabeledTextBox(grpStatic, "默认网关：", 94, "选填，例如 192.168.1.1");
            _txtDns1 = AddLabeledTextBox(grpStatic, "首选 DNS：", 126, "选填，例如 223.5.5.5");
            _txtDns2 = AddLabeledTextBox(grpStatic, "备用 DNS：", 158, "选填，例如 8.8.8.8");

            _btnSaveOnly = new Button
            {
                Text = "仅保存参数",
                Location = new Point(345, 184),
                Size = new Size(105, 25),
                FlatStyle = FlatStyle.System
            };
            _btnSaveOnly.Click += (s, e) => OnSaveOnlyClicked();
            grpStatic.Controls.Add(_btnSaveOnly);

            var lblHint = new Label
            {
                Text = "提示：切换为手动时也会自动保存参数",
                Location = new Point(15, 187),
                Size = new Size(320, 20),
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular)
            };
            grpStatic.Controls.Add(lblHint);

            Controls.Add(grpStatic);

            // 4. Auto-start & tray option row
            _chkAutoStart = new CheckBox
            {
                Text = "开机自动启动（开机后直接静默驻留右下角托盘，不弹窗口）",
                Location = new Point(padX + 2, 403),
                Size = new Size(460, 24),
                ForeColor = Color.FromArgb(45, 50, 55)
            };
            _chkAutoStart.CheckedChanged += (s, e) => OnAutoStartCheckedChanged();
            Controls.Add(_chkAutoStart);

            // 5. Two primary switch buttons
            _btnSwitchDhcp = new Button
            {
                Text = "切换为自动 (DHCP)",
                Location = new Point(padX, 434),
                Size = new Size(222, 42),
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(25, 135, 84),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSwitchDhcp.FlatAppearance.BorderSize = 0;
            _btnSwitchDhcp.Click += (s, e) => OnSwitchToDhcpClicked(false);
            Controls.Add(_btnSwitchDhcp);

            _btnSwitchStatic = new Button
            {
                Text = "切换为手动 (静态 IP)",
                Location = new Point(padX + 243, 434),
                Size = new Size(222, 42),
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSwitchStatic.FlatAppearance.BorderSize = 0;
            _btnSwitchStatic.Click += (s, e) => OnSwitchToStaticClicked(false);
            Controls.Add(_btnSwitchStatic);

            // 6. Status bar message
            _lblStatusMessage = new Label
            {
                Text = isAdmin
                    ? "就绪：点击右上角 × 可隐藏到系统托盘，右键托盘图标可快速切换或退出。"
                    : "提示：当前未获取管理员权限，点击切换按钮时将请求管理员权限。",
                Location = new Point(padX, 482),
                Size = new Size(contentWidth, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = isAdmin ? Color.FromArgb(60, 64, 67) : Color.FromArgb(180, 80, 0)
            };
            Controls.Add(_lblStatusMessage);
        }

        private void InitializeTray()
        {
            _iconDhcp = TrayIconFactory.CreateModeIcon(true);
            _iconStatic = TrayIconFactory.CreateModeIcon(false);
            Icon = _iconDhcp;

            _trayMenu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular)
            };

            _menuItemStatus = new ToolStripMenuItem("当前状态：正在读取...")
            {
                Enabled = false
            };
            _trayMenu.Items.Add(_menuItemStatus);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _menuItemDhcp = new ToolStripMenuItem("切换为自动 (DHCP)");
            _menuItemDhcp.Click += (s, e) => OnSwitchToDhcpClicked(true);
            _trayMenu.Items.Add(_menuItemDhcp);

            _menuItemStatic = new ToolStripMenuItem("切换为手动 (静态 IP)");
            _menuItemStatic.Click += (s, e) => OnSwitchToStaticClicked(true);
            _trayMenu.Items.Add(_menuItemStatic);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _menuItemAutoStart = new ToolStripMenuItem("开机自动启动（静默进托盘）");
            _menuItemAutoStart.Click += (s, e) =>
            {
                _chkAutoStart.Checked = !_chkAutoStart.Checked;
            };
            _trayMenu.Items.Add(_menuItemAutoStart);

            _menuItemOpen = new ToolStripMenuItem("打开设置窗口...");
            _menuItemOpen.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            _menuItemOpen.Click += (s, e) => RestoreFromTray();
            _trayMenu.Items.Add(_menuItemOpen);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _menuItemQuit = new ToolStripMenuItem("退出 (Quit)");
            _menuItemQuit.Click += (s, e) => QuitApplication();
            _trayMenu.Items.Add(_menuItemQuit);

            _notifyIcon = new NotifyIcon
            {
                Icon = _iconDhcp,
                Text = "Windows 11 IP 一键切换",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (Visible && WindowState != FormWindowState.Minimized)
                    {
                        HideToTray(false);
                    }
                    else
                    {
                        RestoreFromTray();
                    }
                }
            };
        }

        public void HideToTray(bool showFirstTimeBalloon)
        {
            ShowInTaskbar = false;
            Hide();
            if (showFirstTimeBalloon && !_hasShownTrayBalloon && _notifyIcon != null)
            {
                _hasShownTrayBalloon = true;
                _notifyIcon.ShowBalloonTip(
                    2500,
                    "已最小化到系统托盘",
                    "左键点击图标可打开主窗口，右键可一键切换 IP 或选择“退出 (Quit)”。",
                    ToolTipIcon.Info);
            }
        }

        public void RestoreFromTray()
        {
            RefreshAdapterList();
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }

        public void QuitApplication()
        {
            _allowExit = true;
            if (_showWindowEvent != null)
            {
                try { _showWindowEvent.Set(); } catch { }
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _allowExit = true;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
                if (_iconDhcp != null)
                {
                    _iconDhcp.Dispose();
                    _iconDhcp = null;
                }
                if (_iconStatic != null)
                {
                    _iconStatic.Dispose();
                    _iconStatic = null;
                }
            }
            base.Dispose(disposing);
        }

        private void SyncAutoStartCheckBoxState()
        {
            _suppressAutoStartEvent = true;
            try
            {
                bool enabled = AutoStartHelper.IsAutoStartEnabled();
                _chkAutoStart.Checked = enabled;
                if (_menuItemAutoStart != null)
                {
                    _menuItemAutoStart.Checked = enabled;
                }
            }
            finally
            {
                _suppressAutoStartEvent = false;
            }
        }

        private void OnAutoStartCheckedChanged()
        {
            if (_suppressAutoStartEvent)
                return;

            bool target = _chkAutoStart.Checked;
            string msg;
            bool ok = AutoStartHelper.SetAutoStart(target, Application.ExecutablePath, out msg);
            if (!ok)
            {
                SyncAutoStartCheckBoxState();
                SetStatusMessage("设置开机自启动失败：" + msg, Color.Red);
                if (Visible)
                {
                    MessageBox.Show(this, "设置开机自启动失败：\r\n" + msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            if (_menuItemAutoStart != null)
            {
                _menuItemAutoStart.Checked = target;
            }
            SetStatusMessage("✓ " + msg, Color.FromArgb(25, 135, 84));
        }

        private TextBox AddLabeledTextBox(Control parent, string labelText, int top, string tooltip)
        {
            var lbl = new Label
            {
                Text = labelText,
                Location = new Point(15, top + 3),
                Size = new Size(90, 22),
                TextAlign = ContentAlignment.MiddleLeft
            };
            parent.Controls.Add(lbl);

            var txt = new TextBox
            {
                Location = new Point(110, top),
                Size = new Size(340, 25)
            };
            var tip = new ToolTip();
            tip.SetToolTip(txt, tooltip);
            parent.Controls.Add(txt);
            return txt;
        }

        private void LoadAdaptersAndConfig()
        {
            SetFormFields(_config.IpAddress, _config.SubnetMask, _config.Gateway, _config.PrimaryDns, _config.SecondaryDns);
            SyncAutoStartCheckBoxState();
            RefreshAdapterList();
        }

        private void RefreshAdapterList()
        {
            string currentSelected = SelectedAdapterName;
            bool isStartupOrNoSelection = string.IsNullOrWhiteSpace(currentSelected);
            string preferredName = isStartupOrNoSelection ? _config.AdapterName : currentSelected;

            _adapters = NetworkHelper.GetAdapters();
            _cmbAdapters.Items.Clear();

            for (int i = 0; i < _adapters.Count; i++)
            {
                _cmbAdapters.Items.Add(_adapters[i]);
            }

            int selectedIdx = NetworkHelper.ChooseBestAdapterIndex(_adapters, preferredName, isStartupOrNoSelection);

            if (selectedIdx >= 0 && selectedIdx < _adapters.Count)
            {
                _cmbAdapters.SelectedIndex = selectedIdx;
            }
            else
            {
                UpdateCurrentAdapterStatusView();
            }
        }

        private void UpdateCurrentAdapterStatusView()
        {
            var adapter = _cmbAdapters.SelectedItem as AdapterStatus;
            if (adapter == null)
            {
                _lblModeBadge.Text = "● 未检测到可用网卡";
                _lblModeBadge.ForeColor = Color.Red;
                _lblCurrentDetails.Text = "请检查网络连接或网卡驱动状态。";
                UpdateTrayVisuals(null);
                return;
            }

            if (adapter.IsDhcpEnabled)
            {
                _lblModeBadge.Text = "● 当前模式：自动获取 (DHCP)";
                _lblModeBadge.ForeColor = Color.FromArgb(25, 135, 84);
            }
            else
            {
                _lblModeBadge.Text = "● 当前模式：手动配置 (静态 IP)";
                _lblModeBadge.ForeColor = Color.FromArgb(13, 110, 253);
            }

            string ipText = string.IsNullOrEmpty(adapter.CurrentIp) ? "正在获取..." : adapter.CurrentIp;
            string maskText = string.IsNullOrEmpty(adapter.CurrentMask) ? "-" : adapter.CurrentMask;
            string gwText = string.IsNullOrEmpty(adapter.CurrentGateway) ? "无" : adapter.CurrentGateway;
            string dnsText = string.IsNullOrEmpty(adapter.CurrentDns) ? "自动" : adapter.CurrentDns;

            _lblCurrentDetails.Text = string.Format(
                "当前 IP：{0}    子网掩码：{1}\r\n默认网关：{2}    DNS 服务器：{3}",
                ipText, maskText, gwText, dnsText);

            UpdateTrayVisuals(adapter);
        }

        private void UpdateTrayVisuals(AdapterStatus adapter)
        {
            if (_notifyIcon == null)
                return;

            string presetIp = _txtIp != null && !string.IsNullOrWhiteSpace(_txtIp.Text)
                ? _txtIp.Text.Trim()
                : (_config != null ? _config.IpAddress : "");

            if (_menuItemStatic != null)
            {
                _menuItemStatic.Text = string.IsNullOrWhiteSpace(presetIp)
                    ? "切换为手动 (静态 IP)"
                    : string.Format("切换为手动 (静态 IP: {0})", presetIp);
            }

            if (adapter == null)
            {
                _notifyIcon.Text = "Windows 11 IP 切换：未检测到网卡";
                if (_menuItemStatus != null) _menuItemStatus.Text = "当前状态：未检测到网卡";
                if (_menuItemDhcp != null) _menuItemDhcp.Checked = false;
                if (_menuItemStatic != null) _menuItemStatic.Checked = false;
                return;
            }

            Icon targetIcon = adapter.IsDhcpEnabled ? _iconDhcp : _iconStatic;
            if (targetIcon != null)
            {
                _notifyIcon.Icon = targetIcon;
                Icon = targetIcon;
            }

            string modeShort = adapter.IsDhcpEnabled ? "自动 (DHCP)" : "手动 (静态 IP)";
            string ipShort = string.IsNullOrEmpty(adapter.CurrentIp) ? "无 IP" : adapter.CurrentIp;
            string tip = string.Format("{0}：{1}\r\nIP: {2}", adapter.Name, modeShort, ipShort);
            if (tip.Length >= 63)
            {
                tip = tip.Substring(0, 62);
            }
            _notifyIcon.Text = tip;

            if (_menuItemStatus != null)
            {
                _menuItemStatus.Text = string.Format("当前：{0} - {1} [{2}]", adapter.Name, modeShort, ipShort);
            }
            if (_menuItemDhcp != null)
            {
                _menuItemDhcp.Checked = adapter.IsDhcpEnabled;
            }
            if (_menuItemStatic != null)
            {
                _menuItemStatic.Checked = !adapter.IsDhcpEnabled;
            }
        }

        private void CopyCurrentAdapterToForm()
        {
            var adapter = _cmbAdapters.SelectedItem as AdapterStatus;
            if (adapter == null)
                return;

            if (!string.IsNullOrWhiteSpace(adapter.CurrentIp))
                _txtIp.Text = adapter.CurrentIp;
            if (!string.IsNullOrWhiteSpace(adapter.CurrentMask))
                _txtMask.Text = adapter.CurrentMask;
            if (!string.IsNullOrWhiteSpace(adapter.CurrentGateway))
                _txtGateway.Text = adapter.CurrentGateway;

            if (!string.IsNullOrWhiteSpace(adapter.CurrentDns))
            {
                string[] parts = adapter.CurrentDns.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) _txtDns1.Text = parts[0].Trim();
                if (parts.Length >= 2) _txtDns2.Text = parts[1].Trim();
            }

            SetStatusMessage("已将当前网卡实时参数填入表单，可修改后保存或切换。", Color.FromArgb(40, 90, 160));
        }

        private void OnSaveOnlyClicked()
        {
            AppConfig cfg = CollectFormConfig();
            string err;
            if (!NetworkHelper.ValidateStaticConfig(cfg, out err))
            {
                MessageBox.Show(this, err, "参数格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            cfg.Save(_configPath);
            _config = cfg;
            UpdateCurrentAdapterStatusView();
            SetStatusMessage("✓ 手动配置参数已保存，下次打开或托盘右键可直接点击切换。", Color.FromArgb(25, 135, 84));
        }

        private void OnSwitchToDhcpClicked(bool fromTray)
        {
            string adapterName = SelectedAdapterName;
            if (string.IsNullOrWhiteSpace(adapterName))
            {
                RestoreFromTray();
                MessageBox.Show(this, "请先选择目标网卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _config.AdapterName = adapterName;
            _config.Save(_configPath);

            if (!EnsureAdminOrRestart(fromTray, "--do-dhcp"))
                return;

            SetButtonsEnabled(false);
            SetStatusMessage("正在同步系统网络属性并切换为自动获取 (DHCP)，请稍候...", Color.FromArgb(40, 90, 160));
            Application.DoEvents();

            try
            {
                string diag;
                bool ok = NetworkHelper.ApplyDhcpConfiguration(adapterName, out diag);
                RefreshAdapterList();

                if (ok)
                {
                    SetStatusMessage("✓ 已成功切换为自动获取 (DHCP)！", Color.FromArgb(25, 135, 84));
                    if (fromTray && _notifyIcon != null)
                    {
                        var cur = _cmbAdapters.SelectedItem as AdapterStatus;
                        string ipInfo = (cur != null && !string.IsNullOrEmpty(cur.CurrentIp)) ? (" (" + cur.CurrentIp + ")") : "";
                        _notifyIcon.ShowBalloonTip(2000, "已切换为自动 (DHCP)", adapterName + " 已启用 DHCP 自动获取" + ipInfo, ToolTipIcon.Info);
                    }
                }
                else
                {
                    SetStatusMessage("切换 DHCP 未完全生效，请查看提示。", Color.Red);
                    MessageBox.Show(this, "切换自动获取 (DHCP) 详情：\r\n" + diag, "切换提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void OnSwitchToStaticClicked(bool fromTray)
        {
            AppConfig cfg = CollectFormConfig();
            string err;
            if (!NetworkHelper.ValidateStaticConfig(cfg, out err))
            {
                RestoreFromTray();
                MessageBox.Show(this, err, "参数校验未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            cfg.Save(_configPath);
            _config = cfg;

            if (!EnsureAdminOrRestart(fromTray, "--do-static"))
                return;

            SetButtonsEnabled(false);
            SetStatusMessage("正在应用手动静态 IP 配置并同步系统设置，请稍候...", Color.FromArgb(40, 90, 160));
            Application.DoEvents();

            try
            {
                string diag;
                bool ok = NetworkHelper.ApplyStaticConfiguration(cfg, out diag);
                RefreshAdapterList();

                if (ok)
                {
                    SetStatusMessage("✓ 已成功切换为手动配置 (静态 IP) 并保存参数！", Color.FromArgb(13, 110, 253));
                    if (fromTray && _notifyIcon != null)
                    {
                        _notifyIcon.ShowBalloonTip(2000, "已切换为手动 (静态 IP)", cfg.AdapterName + " -> " + cfg.IpAddress, ToolTipIcon.Info);
                    }
                }
                else
                {
                    SetStatusMessage("切换静态 IP 失败，请查看详情。", Color.Red);
                    MessageBox.Show(this, "切换手动静态 IP 失败：\r\n" + diag, "切换失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private bool EnsureAdminOrRestart(bool fromTray, string actionFlag)
        {
            if (NetworkHelper.IsAdministrator())
                return true;

            Program.ReleaseCurrentMutex();
            if (Program.TryRelaunchAsAdmin(fromTray, actionFlag))
            {
                QuitApplication();
            }
            else
            {
                Program.ReacquireCurrentMutex();
            }
            return false;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _btnSwitchDhcp.Enabled = enabled;
            _btnSwitchStatic.Enabled = enabled;
            _btnSaveOnly.Enabled = enabled;
            _btnRefresh.Enabled = enabled;
            _cmbAdapters.Enabled = enabled;
            if (_menuItemDhcp != null) _menuItemDhcp.Enabled = enabled;
            if (_menuItemStatic != null) _menuItemStatic.Enabled = enabled;
        }

        private void SetStatusMessage(string msg, Color color)
        {
            _lblStatusMessage.Text = msg;
            _lblStatusMessage.ForeColor = color;
        }
    }

    public static class SelfTestRunner
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + message);
            }
        }

        public static int Run()
        {
            try
            {
                // 1. Strict IPv4 validation (Review Focus #2)
                Assert(NetworkHelper.IsValidIpv4("192.168.1.200"), "Valid IP should pass");
                Assert(NetworkHelper.IsValidIpv4(" 10.0.0.1 "), "Trimmed IP should pass");
                Assert(!NetworkHelper.IsValidIpv4("12345"), "Single integer must fail");
                Assert(!NetworkHelper.IsValidIpv4("192.168.1"), "3-octet string must fail");
                Assert(!NetworkHelper.IsValidIpv4("192.168.1.256"), "Out of range octet must fail");
                Assert(!NetworkHelper.IsValidIpv4("192.168.01.1"), "Leading zero octet must fail");

                // 2. Quoted adapter names with spaces/Chinese (Review Focus #1)
                Assert("以太网".Length == 3, "Chinese string literal encoding check: expected length 3, got " + "以太网".Length);
                string[] dhcpCmds = NetworkHelper.BuildDhcpCommands("以太网 2");
                Assert(dhcpCmds.Length == 2, "DHCP should build 2 commands");
                Assert(dhcpCmds[0] == "interface ipv4 set address name=\"以太网 2\" source=dhcp", "DHCP address command mismatch");
                Assert(dhcpCmds[1] == "interface ipv4 set dnsservers name=\"以太网 2\" source=dhcp", "DHCP dns command mismatch");

                // 3. Static commands with full parameters and with optional gateway/DNS empty (Review Focus #3)
                var fullCfg = new AppConfig
                {
                    AdapterName = "WLAN",
                    IpAddress = "192.168.1.150",
                    SubnetMask = "255.255.255.0",
                    Gateway = "192.168.1.1",
                    PrimaryDns = "223.5.5.5",
                    SecondaryDns = "8.8.8.8"
                };
                string err;
                Assert(NetworkHelper.ValidateStaticConfig(fullCfg, out err), "Full config should validate: " + err);
                string[] fullCmds = NetworkHelper.BuildStaticCommands(fullCfg);
                Assert(fullCmds.Length == 3, "Full config should build 3 commands");
                Assert(fullCmds[0] == "interface ipv4 set address name=\"WLAN\" static 192.168.1.150 255.255.255.0 192.168.1.1", "Static address with gateway mismatch");
                Assert(fullCmds[1] == "interface ipv4 set dnsservers name=\"WLAN\" static 223.5.5.5 primary validate=no", "Primary DNS command mismatch");
                Assert(fullCmds[2] == "interface ipv4 add dnsservers name=\"WLAN\" 8.8.8.8 index=2 validate=no", "Secondary DNS command mismatch");

                var noGwNoDnsCfg = new AppConfig
                {
                    AdapterName = "以太网",
                    IpAddress = "10.0.0.5",
                    SubnetMask = "255.255.255.0",
                    Gateway = "",
                    PrimaryDns = "",
                    SecondaryDns = ""
                };
                Assert(NetworkHelper.ValidateStaticConfig(noGwNoDnsCfg, out err), "No-gateway config should validate");
                string[] minCmds = NetworkHelper.BuildStaticCommands(noGwNoDnsCfg);
                Assert(minCmds.Length == 2, "Minimal static config should build 2 commands");
                Assert(minCmds[0] == "interface ipv4 set address name=\"以太网\" static 10.0.0.5 255.255.255.0", "Static address without gateway mismatch");
                Assert(minCmds[1] == "interface ipv4 set dnsservers name=\"以太网\" source=dhcp", "Empty DNS should fallback to dhcp");

                // 4. Config persistence & corrupted file fallback (Review Focus #4)
                string tempFile = Path.Combine(Path.GetTempPath(), "ip_switcher_test_" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    AppConfig missingCfg = AppConfig.Load(tempFile);
                    Assert(missingCfg.SubnetMask == "255.255.255.0", "Missing config should default subnet mask");

                    fullCfg.Save(tempFile);
                    AppConfig loadedCfg = AppConfig.Load(tempFile);
                    Assert(loadedCfg.AdapterName == "WLAN" && loadedCfg.IpAddress == "192.168.1.150" && loadedCfg.SecondaryDns == "8.8.8.8", "Saved config should round-trip");

                    File.WriteAllText(tempFile, "{corrupted json", Encoding.UTF8);
                    AppConfig corruptedCfg = AppConfig.Load(tempFile);
                    Assert(corruptedCfg.SubnetMask == "255.255.255.0", "Corrupted config should fallback cleanly");
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }

                // 5. Adapter enumeration check
                List<AdapterStatus> adapters = NetworkHelper.GetAdapters();
                Assert(adapters != null, "GetAdapters should not return null");

                // 6. Windows 11 WlanSvc ProfileIpConfigurationV4 & helper checks
                Assert(NetworkHelper.DecodeDhcpNetworkHint("B4963756B696D25374") == "Kiseki-5G", "DecodeDhcpNetworkHint failed");
                Assert(NetworkHelper.SubnetMaskToPrefixLength("255.255.255.0") == 24, "SubnetMaskToPrefixLength /24 failed");
                Assert(NetworkHelper.SubnetMaskToPrefixLength("255.255.0.0") == 16, "SubnetMaskToPrefixLength /16 failed");

                byte[] dhcpBlob = NetworkHelper.BuildWlanProfileIpConfigBlob(null);
                Assert(dhcpBlob != null && dhcpBlob.Length == 28, "DHCP WlanProfileIpConfigBlob must be 28 bytes");

                byte[] staticBlob = NetworkHelper.BuildWlanProfileIpConfigBlob(fullCfg);
                Assert(staticBlob != null && staticBlob.Length == 8764, "Static WlanProfileIpConfigBlob with 2 DNS must be 8764 bytes");
                Assert(staticBlob[32] == 192 && staticBlob[33] == 168 && staticBlob[34] == 1 && staticBlob[35] == 150, "Static blob IP bytes mismatch");
                Assert(staticBlob[156] == 24, "Static blob prefix length mismatch");

                // 7. System Tray & AutoStart helper checks
                string runRegVal = AutoStartHelper.BuildRunRegistryValue(@"C:\Tools\IpSwitcher.exe");
                Assert(runRegVal == "\"C:\\Tools\\IpSwitcher.exe\" --tray", "AutoStart Run registry value mismatch: " + runRegVal);
                using (Icon dhcpIcon = TrayIconFactory.CreateModeIcon(true))
                using (Icon staticIcon = TrayIconFactory.CreateModeIcon(false))
                {
                    Assert(dhcpIcon != null && dhcpIcon.Width > 0, "DHCP tray icon should be created");
                    Assert(staticIcon != null && staticIcon.Width > 0, "Static tray icon should be created");
                }

                // 8. Smart adapter priority sorting & startup selection checks
                Assert(NetworkHelper.IsLikelyVirtualAdapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter"), "WSL should be detected as virtual");
                Assert(NetworkHelper.IsLikelyVirtualAdapter("Meta", "Meta Tunnel") == false && NetworkHelper.IsLikelyVirtualAdapter("Clash", "Meta WinTun"), "WinTun/Clash should be detected as virtual");
                Assert(!NetworkHelper.IsLikelyVirtualAdapter("WLAN", "Intel(R) Wi-Fi 6E AX211 160MHz"), "WLAN should be detected as physical");

                var mockList = new List<AdapterStatus>
                {
                    new AdapterStatus { Name = "VMware Network Adapter VMnet8", Description = "VMware Virtual Ethernet Adapter", Status = OperationalStatus.Up, CurrentIp = "192.168.80.1", CurrentGateway = "" },
                    new AdapterStatus { Name = "以太网", Description = "Realtek PCIe GbE Family Controller", Status = OperationalStatus.Down, CurrentIp = "", CurrentGateway = "" },
                    new AdapterStatus { Name = "WLAN", Description = "Intel(R) Wi-Fi 6E AX211", Status = OperationalStatus.Up, CurrentIp = "192.168.110.24", CurrentGateway = "192.168.110.1" }
                };
                List<AdapterStatus> sortedMock = NetworkHelper.SortAdaptersByPriority(mockList);
                Assert(sortedMock[0].Name == "WLAN", "Connected physical adapter with gateway should be sorted first, got: " + sortedMock[0].Name);
                Assert(sortedMock[1].Name == "VMware Network Adapter VMnet8", "Connected virtual adapter should sort after physical Up adapter");
                Assert(sortedMock[2].Name == "以太网", "Disconnected adapter should sort last");

                int idxWhenSavedDown = NetworkHelper.ChooseBestAdapterIndex(sortedMock, "以太网", true);
                Assert(sortedMock[idxWhenSavedDown].Name == "WLAN", "When saved adapter is Down on startup, should auto-select connected physical adapter WLAN");

                int idxWhenSavedUp = NetworkHelper.ChooseBestAdapterIndex(sortedMock, "WLAN", true);
                Assert(sortedMock[idxWhenSavedUp].Name == "WLAN", "When saved adapter is Up on startup, should select saved adapter");

                Console.WriteLine("Self-test passed (" + adapters.Count + " adapters detected).");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        public static int RunSmokeUi()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "ip_switcher_ui_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var seed = new AppConfig
                {
                    AdapterName = "WLAN",
                    IpAddress = "192.168.50.88",
                    SubnetMask = "255.255.255.0",
                    Gateway = "192.168.50.1",
                    PrimaryDns = "114.114.114.114",
                    SecondaryDns = "8.8.8.8"
                };
                seed.Save(tempFile);

                using (var form = new MainForm(tempFile, false, null))
                {
                    AppConfig collected = form.CollectFormConfig();
                    Assert(collected.IpAddress == "192.168.50.88", "Form should load seeded IP");
                    Assert(collected.Gateway == "192.168.50.1", "Form should load seeded Gateway");
                    Assert(form.TrayNotifyIcon != null && form.TrayNotifyIcon.ContextMenuStrip != null, "Tray icon and context menu should be initialized");
                    Assert(form.AutoStartCheckBox != null, "Auto-start checkbox should be initialized");

                    form.SetFormFields("192.168.50.99", "255.255.255.0", "192.168.50.1", "223.5.5.5", "");
                    form.SaveCurrentFormToConfig();
                }

                using (var trayForm = new MainForm(tempFile, true, null))
                {
                    Assert(trayForm.StartInTray, "Tray form should record startInTray=true");
                }

                AppConfig afterSave = AppConfig.Load(tempFile);
                Assert(afterSave.IpAddress == "192.168.50.99", "Form save should persist updated IP");
                Assert(afterSave.PrimaryDns == "223.5.5.5", "Form save should persist updated DNS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\Win11IpSwitcher_SingleInstance_Mutex";
        private const string ShowWindowEventName = @"Local\Win11IpSwitcher_ShowWindow_Event";
        private static Mutex _activeMutex;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void ReleaseCurrentMutex()
        {
            try
            {
                if (_activeMutex != null)
                {
                    _activeMutex.ReleaseMutex();
                }
            }
            catch
            {
            }
        }

        public static void ReacquireCurrentMutex()
        {
            try
            {
                if (_activeMutex != null)
                {
                    _activeMutex.WaitOne(1000);
                }
            }
            catch
            {
            }
        }

        public static bool TryRelaunchAsAdmin(bool startInTray, string actionFlag = null)
        {
            try
            {
                string extra = startInTray ? " --tray" : "";
                if (!string.IsNullOrWhiteSpace(actionFlag))
                {
                    extra += " " + actionFlag;
                }
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--elevated-attempt" + extra,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Contains("--self-test"))
            {
                return SelfTestRunner.Run();
            }

            AutoStartHelper.TryRemoveZoneIdentifier(Application.ExecutablePath);

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_run.log");
                    File.AppendAllText(logPath, "[ThreadException] " + e.Exception + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_run.log");
                    File.AppendAllText(logPath, "[UnhandledException] " + e.ExceptionObject + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            };

            if (args != null && args.Contains("--smoke-ui"))
            {
                return SelfTestRunner.RunSmokeUi();
            }

            bool startInTray = args != null && args.Contains("--tray");
            bool skipAutoElevate = args != null && (args.Contains("--no-elevate") || args.Contains("--elevated-attempt"));
            string initialAction = null;
            if (args != null)
            {
                if (args.Contains("--do-dhcp")) initialAction = "--do-dhcp";
                else if (args.Contains("--do-static")) initialAction = "--do-static";
            }

            // Check if an instance is already running before requesting elevation
            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            bool createdNew;
            using (Mutex singleMutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                _activeMutex = singleMutex;
                if (!createdNew)
                {
                    // Another instance is already running in tray/window: wake it up and exit
                    try { showEvent.Set(); } catch { }
                    return 0;
                }

                // When started silently at boot via HKCU\...\Run (--tray), do NOT prompt UAC on boot;
                // elevate on demand only when the user clicks to switch IP mode.
                if (!startInTray && !NetworkHelper.IsAdministrator() && !skipAutoElevate)
                {
                    // Release mutex before relaunching elevated child so child can acquire it
                    ReleaseCurrentMutex();
                    if (TryRelaunchAsAdmin(startInTray, initialAction))
                    {
                        return 0;
                    }
                    // If user canceled UAC, re-acquire mutex and continue in preview mode
                    ReacquireCurrentMutex();
                }

                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                Application.Run(new MainForm(configPath, startInTray, showEvent, initialAction));
                GC.KeepAlive(singleMutex);
            }
            return 0;
        }
    }
}
