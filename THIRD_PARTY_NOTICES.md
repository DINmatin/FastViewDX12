# Third-party notices

FastView restores the following dependencies from NuGet. Their own license texts remain authoritative.

| Package | Version in project | License / notes |
|---|---:|---|
| SharpGLTF.Core | 1.0.6 | MIT |
| TinyEXR.NET | 1.0.0 | MIT; wrapper around TinyEXR, whose bundled notices must also be retained where required |
| Vortice.Direct3D12 | 3.8.3 | MIT |
| Vortice.DirectX | 3.8.3 | MIT |
| Vortice.DXGI | 3.8.3 | MIT |
| Microsoft.Direct3D.DXC | 1.9.2602.24 | Package contains MIT- and LLVM-licensed components; retain the license files supplied by the package where redistribution requires it |
| System.Drawing.Common | 10.0.0 | Part of the .NET libraries; see the license supplied with the package/runtime |


Before publishing a binary release, inspect the restored NuGet packages and copy any required license or notice files into the release package. This file is a summary, not a replacement for those license texts.

## Day Environment HDRI 057

FastView includes the 1K EXR version of **Day Environment HDRI 057**
from ambientCG as:

`src/FastViewDX12/Assets/Environment/default.exr`

Source:

https://ambientcg.com/a/DayEnvironmentHDRI057

License:

Creative Commons CC0 1.0 Universal

The asset may be copied, modified, and redistributed, including for
commercial purposes. Attribution is not required, but is included here
in appreciation of ambientCG.