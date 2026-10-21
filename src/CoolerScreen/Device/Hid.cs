using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CoolerScreen.Device
{
    /// <summary>
    /// The few Windows HID calls Cooler Screen needs. No drivers are installed: this is the same
    /// built-in Windows HID support every keyboard and mouse uses.
    /// </summary>
    internal static class Hid
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        private const int DIGCF_PRESENT = 0x2;
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attributes);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle h, byte[] buffer, int length);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetManufacturerString(SafeFileHandle h, byte[] buffer, int length);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        /// <summary>
        /// Lists HID device paths whose path names this vendor and one of these products.
        /// Matching is done on the path text, so no other device is ever opened.
        /// </summary>
        public static List<string> FindPaths(ushort vendor, params ushort[] products)
        {
            var result = new List<string>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
            try
            {
                string vid = "vid_" + vendor.ToString("x4");
                for (int i = 0; ; i++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize is 8 on 64-bit, 6 on 32-bit.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(detail + 4);
                        if (string.IsNullOrEmpty(path)) continue;
                        string lower = path.ToLowerInvariant();
                        if (!lower.Contains(vid)) continue;
                        foreach (var p in products)
                            if (lower.Contains("pid_" + p.ToString("x4"))) { result.Add(path); break; }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        public static bool TryGetCaps(SafeFileHandle h, out HIDP_CAPS caps)
        {
            caps = default;
            if (!HidD_GetPreparsedData(h, out IntPtr pd)) return false;
            try { return HidP_GetCaps(pd, out caps) == HIDP_STATUS_SUCCESS; }
            finally { HidD_FreePreparsedData(pd); }
        }

        public static string Product(SafeFileHandle h) => ReadString(h, HidD_GetProductString);
        public static string Serial(SafeFileHandle h) => ReadString(h, HidD_GetSerialNumberString);
        public static string Manufacturer(SafeFileHandle h) => ReadString(h, HidD_GetManufacturerString);

        private static string ReadString(SafeFileHandle h, Func<SafeFileHandle, byte[], int, bool> call)
        {
            try
            {
                var buf = new byte[256];
                if (!call(h, buf, buf.Length)) return "";
                string s = Encoding.Unicode.GetString(buf);
                int z = s.IndexOf('\0');
                return (z >= 0 ? s.Substring(0, z) : s).Trim();
            }
            catch { return ""; }
        }
    }
}
