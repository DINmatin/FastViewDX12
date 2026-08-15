using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace FastViewDX12;

/// <summary>
/// Receives 3Dconnexion six-axis HID reports through the Windows Raw Input API.
/// This supports classic Logitech-branded SpaceExplorer devices and current
/// 3Dconnexion hardware without requiring a redistributable vendor SDK.
/// </summary>
internal sealed class SpaceMouseInput : IDisposable
{
    private const ushort UsagePageGenericDesktop = 0x01;
    private const ushort UsageMultiAxisController = 0x08;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeHid = 2;
    private const uint RidevRemove = 0x00000001;

    private readonly Dictionary<nint, bool> _recognizedDevices = new();
    private nint _targetWindow;

    public event Action<Vector3, Vector3>? Motion;

    public bool HasReceivedInput { get; private set; }

    public void Register(nint targetWindow)
    {
        if (targetWindow == 0)
        {
            return;
        }

        var device = new RawInputDevice
        {
            UsagePage = UsagePageGenericDesktop,
            Usage = UsageMultiAxisController,
            Flags = 0,
            Target = targetWindow
        };

        if (RegisterRawInputDevices(
                [device],
                1,
                (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            _targetWindow = targetWindow;
        }
    }

    public void ProcessInput(nint rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();

        if (GetRawInputData(
                rawInputHandle,
                RidInput,
                null,
                ref size,
                headerSize) == uint.MaxValue ||
            size < headerSize + 8)
        {
            return;
        }

        byte[] data = new byte[size];

        if (GetRawInputData(
                rawInputHandle,
                RidInput,
                data,
                ref size,
                headerSize) == uint.MaxValue)
        {
            return;
        }

        RawInputHeader header;
        unsafe
        {
            fixed (byte* pointer = data)
            {
                header = Marshal.PtrToStructure<RawInputHeader>((nint)pointer);
            }
        }

        if (header.Type != RimTypeHid ||
            !IsRecognizedDevice(header.Device))
        {
            return;
        }

        int hidOffset = checked((int)headerSize);
        int reportSize = BitConverter.ToInt32(data, hidOffset);
        int reportCount = BitConverter.ToInt32(data, hidOffset + 4);
        int reportOffset = hidOffset + 8;

        if (reportSize <= 0 ||
            reportCount <= 0 ||
            reportOffset + (long)reportSize * reportCount > data.Length)
        {
            return;
        }

        for (int index = 0; index < reportCount; index++)
        {
            ReadOnlySpan<byte> report = data.AsSpan(
                reportOffset + index * reportSize,
                reportSize);

            ParseMotionReport(report);
        }
    }

    private void ParseMotionReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 7)
        {
            return;
        }

        Vector3 translation = Vector3.Zero;
        Vector3 rotation = Vector3.Zero;

        switch (report[0])
        {
            case 1:
                translation = ReadVector(report, 1);

                // Some newer devices combine translation and rotation in one report.
                if (report.Length >= 13)
                {
                    rotation = ReadVector(report, 7);
                }

                break;

            case 2:
                rotation = ReadVector(report, 1);
                break;

            default:
                return;
        }

        HasReceivedInput = true;
        Motion?.Invoke(translation, rotation);
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> report, int offset)
    {
        return new Vector3(
            BitConverter.ToInt16(report.Slice(offset, 2)),
            BitConverter.ToInt16(report.Slice(offset + 2, 2)),
            BitConverter.ToInt16(report.Slice(offset + 4, 2)));
    }

    private bool IsRecognizedDevice(nint deviceHandle)
    {
        if (_recognizedDevices.TryGetValue(deviceHandle, out bool recognized))
        {
            return recognized;
        }

        uint characterCount = 0;
        GetRawInputDeviceInfo(
            deviceHandle,
            RidiDeviceName,
            null,
            ref characterCount);

        var name = new StringBuilder(checked((int)characterCount + 1));

        if (characterCount > 0)
        {
            GetRawInputDeviceInfo(
                deviceHandle,
                RidiDeviceName,
                name,
                ref characterCount);
        }

        string value = name.ToString();
        recognized =
            value.Contains("VID_256F", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("VID_046D&PID_C62", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("3DCONNEXION", StringComparison.OrdinalIgnoreCase);

        _recognizedDevices[deviceHandle] = recognized;
        return recognized;
    }

    public void Dispose()
    {
        if (_targetWindow != 0)
        {
            var device = new RawInputDevice
            {
                UsagePage = UsagePageGenericDesktop,
                Usage = UsageMultiAxisController,
                Flags = RidevRemove,
                Target = 0
            };

            RegisterRawInputDevices(
                [device],
                1,
                (uint)Marshal.SizeOf<RawInputDevice>());
        }

        _targetWindow = 0;
        _recognizedDevices.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        [Out] byte[]? data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(
        nint device,
        uint command,
        StringBuilder? data,
        ref uint size);
}
