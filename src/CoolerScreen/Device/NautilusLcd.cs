using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CoolerScreen.Device
{
    /// <summary>
    /// The CORSAIR NAUTILUS RS LCD pump cap screen (USB 1B1C:0C57, and 1B1C:0C55 on some caps).
    /// Pictures are sent as 480x480 JPEGs, split into 1,024-byte HID output reports.
    /// Brightness, rotation and "hand back to the cooler" are HID feature reports.
    /// </summary>
    public sealed class NautilusLcd : IDisposable
    {
        public const ushort Vendor = 0x1B1C;
        public static readonly ushort[] Products = { 0x0C57, 0x0C55 };

        private const int HeaderSize = 8;
        private const int DefaultReportSize = 1024;
        private const byte FrameReportId = 0x02;
        private const byte FeatureReportId = 0x03;
        private const byte FirmwareReportId = 0x05;

        private readonly SafeFileHandle _control;   // feature reports
        private readonly SafeFileHandle _frames;    // output reports (overlapped, so a stuck write can time out)
        private readonly FileStream _stream;
        private readonly object _gate = new object();
        private readonly bool _useSetOutputReport;

        public string Path { get; }
        public string Product { get; private set; } = "";
        public string Serial { get; private set; } = "";
        public string Manufacturer { get; private set; } = "";
        public string Firmware { get; private set; } = "";
        public ushort ProductId { get; private set; }
        public int OutputReportLength { get; }
        public int FeatureReportLength { get; }

        public int ChunkSize => OutputReportLength - HeaderSize;

        private NautilusLcd(string path, SafeFileHandle control, SafeFileHandle frames, int outLen, int featLen, bool useSetOutputReport)
        {
            Path = path;
            _control = control;
            _frames = frames;
            OutputReportLength = outLen;
            FeatureReportLength = featLen;
            _useSetOutputReport = useSetOutputReport;
            if (!useSetOutputReport) _stream = new FileStream(frames, FileAccess.Write, 0, true);
        }

        /// <summary>Paths of every connected Nautilus LCD cap.</summary>
        public static string[] Find() => Hid.FindPaths(Vendor, Products).ToArray();

        /// <summary>Opens the screen, shared, so nothing else on the PC is ever locked out.</summary>
        public static NautilusLcd Open(string path)
        {
            const uint share = Hid.FILE_SHARE_READ | Hid.FILE_SHARE_WRITE;
            var control = Hid.CreateFile(path, Hid.GENERIC_READ | Hid.GENERIC_WRITE, share, IntPtr.Zero, Hid.OPEN_EXISTING, 0, IntPtr.Zero);
            bool limited = false;
            if (control.IsInvalid)
            {
                // Some HID collections can only be opened without read/write rights; feature and
                // output reports still work through the HidD_ calls.
                control.Dispose();
                control = Hid.CreateFile(path, 0, share, IntPtr.Zero, Hid.OPEN_EXISTING, 0, IntPtr.Zero);
                limited = true;
                if (control.IsInvalid)
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    control.Dispose();
                    throw new IOException("Windows wouldn't open the cooler's screen (error " + err + ").");
                }
            }

            int outLen = DefaultReportSize, featLen = 64;
            if (Hid.TryGetCaps(control, out var caps))
            {
                if (caps.OutputReportByteLength > HeaderSize + 16) outLen = caps.OutputReportByteLength;
                if (caps.FeatureReportByteLength > 4) featLen = caps.FeatureReportByteLength;
            }

            SafeFileHandle frames = null;
            if (!limited)
            {
                frames = Hid.CreateFile(path, Hid.GENERIC_WRITE, share, IntPtr.Zero, Hid.OPEN_EXISTING, Hid.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (frames.IsInvalid) { frames.Dispose(); frames = null; limited = true; }
            }

            var lcd = new NautilusLcd(path, control, frames, outLen, featLen, limited);
            var attr = new Hid.HIDD_ATTRIBUTES { Size = System.Runtime.InteropServices.Marshal.SizeOf<Hid.HIDD_ATTRIBUTES>() };
            if (Hid.HidD_GetAttributes(control, ref attr)) lcd.ProductId = attr.ProductID;
            lcd.Product = Hid.Product(control);
            lcd.Serial = Hid.Serial(control);
            lcd.Manufacturer = Hid.Manufacturer(control);
            lcd.Firmware = lcd.ReadFirmware();
            return lcd;
        }

        private string ReadFirmware()
        {
            try
            {
                var buf = new byte[Math.Max(FeatureReportLength, 33)];
                buf[0] = FirmwareReportId;
                if (!Hid.HidD_GetFeature(_control, buf, FeatureReportLength)) return "";
                string Part(int i)
                {
                    if (i >= buf.Length) return "0";
                    byte b = buf[i];
                    return b >= (byte)'0' && b <= (byte)'9' ? ((char)b).ToString() : b.ToString();
                }
                return Part(6) + "." + Part(8) + "." + Part(10) + "." + Part(12);
            }
            catch { return ""; }
        }

        private bool SendFeature(byte b1, byte b2, byte b3)
        {
            lock (_gate)
            {
                var buf = new byte[FeatureReportLength];
                buf[0] = FeatureReportId;
                buf[1] = b1;
                buf[2] = b2;
                buf[3] = b3;
                return Hid.HidD_SetFeature(_control, buf, buf.Length);
            }
        }

        /// <summary>Backlight from 0 (off) to 100.</summary>
        public bool SetBrightness(int percent) => SendFeature(0x0B, (byte)Math.Max(0, Math.Min(100, percent)), 0x01);

        /// <summary>0 = normal, 1 = 90°, 2 = 180°, 3 = 270°.</summary>
        public bool SetRotation(int quarterTurns) => SendFeature(0x0C, (byte)(((quarterTurns % 4) + 4) % 4), 0x01);

        /// <summary>Gives the screen back to the cooler's own built-in display.</summary>
        public void HandBack()
        {
            SendFeature(0x1E, 0x01, 0x01);
            SendFeature(0x1D, 0x00, 0x01);
        }

        /// <summary>Sends one JPEG picture. The last packet is flagged so the screen shows it.</summary>
        public void SendJpeg(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0) return;
            int chunk = ChunkSize;
            int count = (jpeg.Length + chunk - 1) / chunk;
            if (count > 255) throw new InvalidOperationException("Picture too big to send (" + jpeg.Length / 1024 + " KB).");
            lock (_gate)
            {
                var report = new byte[OutputReportLength];
                for (int i = 0; i < count; i++)
                {
                    int offset = i * chunk;
                    int len = Math.Min(chunk, jpeg.Length - offset);
                    Array.Clear(report, 0, report.Length);
                    report[0] = FrameReportId;
                    report[1] = 0x05;
                    report[3] = (byte)(i == count - 1 ? 0x01 : 0x00);
                    report[4] = (byte)i;
                    report[6] = (byte)(len & 0xFF);
                    report[7] = (byte)((len >> 8) & 0xFF);
                    Buffer.BlockCopy(jpeg, offset, report, HeaderSize, len);
                    Write(report);
                }
            }
        }

        private void Write(byte[] report)
        {
            if (_useSetOutputReport)
            {
                if (!Hid.HidD_SetOutputReport(_control, report, report.Length))
                    throw new IOException("The cooler's screen stopped answering (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").");
                return;
            }
            using var cts = new CancellationTokenSource(1500);
            try
            {
                _stream.WriteAsync(report, 0, report.Length, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new IOException("The cooler's screen didn't take the picture in time.");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _stream?.Dispose(); } catch { }
                try { _frames?.Dispose(); } catch { }
                try { _control?.Dispose(); } catch { }
            }
        }
    }
}
