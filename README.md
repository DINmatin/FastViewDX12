# FastView

FastView is a small Windows 3D viewer for **GLB** and **glTF** files. It uses WinForms and Direct3D 12 and includes Windows Explorer thumbnail providers.

![FastViewDX12 preview](docs/images/fastview-preview.png)

## Current features

- Open `.glb` and `.gltf` files from the command line, drag-and-drop, or **Open with**.
- Direct3D 12 rendering with PBR-style material support.
- EXR environment lighting.
- Visible viewer background can be either:
  - a picked solid color, or
  - an EXR environment with an opacity control.
- Explorer thumbnails:
  - transparent background,
  - automatic crop with a tighter model fit,
  - 45-degree three-quarter camera view,
  - support for `.glb`,
  - support for `.gltf`, including adjacent `.bin` and texture files, through a file-based provider.
- Multi-resolution GLB/glTF fallback icon from 16 to 256 px.
- Inno Setup installer build.

## Requirements

### End users

- Windows 10 or newer, x64
- Microsoft .NET 10 Runtime (x64) for the Windows Explorer thumbnail provider

The viewer is published self-contained. The COM thumbnail provider remains framework-dependent because .NET COM hosting does not support self-contained deployment. The installer checks for the x64 .NET 10 runtime before installing.

### Developers

- Visual Studio 2022 with the .NET desktop development workload, or the .NET 10 SDK
- Inno Setup 6.3 or newer for installer builds

NuGet restore supplies the DirectX Shader Compiler used to compile the HLSL files during the build. Generated `.cso` files are intentionally not stored in Git.

## Build

Open `FastView.slnx` in Visual Studio and build the solution, or run:

```powershell
dotnet build .\FastView.slnx -c Release
```

Create a self-contained release payload and ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\BuildRelease.ps1 -Version 1.1.0
```

Create the Windows installer:

```text
installer\BuildInstaller.cmd
```

The build scripts use only repository-relative paths. Output is written to `artifacts/` and `installer/Output/`.

## Thumbnail-provider architecture

`.glb` files are self-contained, so their provider uses `IInitializeWithStream` and writes the supplied stream to a temporary GLB.

`.gltf` files may reference neighboring `.bin`, `.png`, `.jpg`, or other files. Their provider therefore uses `IInitializeWithFile` and passes the original file path to FastView. The installer registers a separate CLSID for this provider.

## Manual thumbnail test

```bat
FastViewDX12.exe --thumbnail "C:\path\model.glb" "C:\Temp\FastViewTest.png" 768 768
```

The output PNG should contain the model on a transparent background.

## Repository layout

```text
src/FastViewDX12/                 Viewer and thumbnail renderer
src/FastView.ThumbnailProvider/   Explorer COM thumbnail providers
build/                            Reproducible release scripts
installer/                        Inno Setup project
docs/                             Testing and release notes
```

## Free sample model

A free GLB model is included for testing FastViewDX12:

[Download FastViewDemo.glb](samples/FastViewDemo.glb)

The sample model is released under CC0 1.0. See
[`samples/README.md`](samples/README.md) for details.

### License

FastViewDX12 and its source code are licensed under the
[MIT License](LICENSE).

The sample model in `samples/` is released separately under the
Creative Commons CC0 1.0 Universal Public Domain Dedication.

Third-party assets and package information are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Developer documentation

- [Architecture and ownership](docs/ARCHITECTURE.md)
- [Refactor notes and validation](docs/REFACTOR_NOTES.md)
- [Testing](docs/TESTING.md)
- [Release checklist](docs/RELEASE_CHECKLIST.md)
