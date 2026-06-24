using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FastView.ThumbnailProvider;

/// <summary>
/// Windows Shell initialization contract used when Explorer supplies a file as a COM stream.
/// </summary>
[ComVisible(true)]
[Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize(
        IStream stream,
        uint mode);
}

/// <summary>
/// Windows Shell initialization contract used when the original path is required for external glTF dependencies.
/// </summary>
[ComVisible(true)]
[Guid("B7D14566-0509-4CCE-A71F-0A554233BD9B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithFile
{
    [PreserveSig]
    int Initialize(
        [MarshalAs(UnmanagedType.LPWStr)]
        string filePath,
        uint mode);
}

/// <summary>
/// Windows Shell contract that returns one HBITMAP thumbnail and describes its alpha representation.
/// </summary>
[ComVisible(true)]
[Guid("E357FCCD-A995-4576-B01F-234630154E96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(
        uint size,
        out IntPtr bitmapHandle,
        out WtsAlphaType alphaType);
}

/// <summary>
/// Describes whether an Explorer thumbnail contains no alpha, opaque RGB, or premultiplied ARGB.
/// </summary>
public enum WtsAlphaType
{
    Unknown = 0,
    Rgb = 1,
    Argb = 2
}
