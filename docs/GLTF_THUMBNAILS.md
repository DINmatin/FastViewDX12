# Why glTF uses a separate thumbnail provider

A GLB contains its scene data in one binary file. Windows can safely initialize the GLB thumbnail provider with a stream.

A `.gltf` file is JSON and commonly references sibling files such as:

```text
model.gltf
model.bin
baseColor.png
normal.png
```

Copying only the JSON stream to a temporary file breaks those relative references. FastView therefore registers a second provider that implements `IInitializeWithFile` and receives the original `.gltf` path.

Provider CLSIDs:

```text
GLB:  {A2A86C88-5B89-4D5E-92B3-7A6CF4E0A1B7}
glTF: {6B07920A-CFEC-42E6-B718-73A199156EFD}
```

The installer sets `DisableProcessIsolation=1` for the file-based glTF provider so Windows can initialize it with the original path. The GLB provider remains stream-based and isolated.
