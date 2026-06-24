# Public release checklist

- [ ] Choose and add a project `LICENSE` file.
- [ ] Confirm copyright and redistribution rights for:
  - [ ] `Assets/Environment/default.exr`
  - [ ] `Assets/Icons/FastView.ico`
  - [ ] `Assets/Icons/GlbFile.ico`
- [ ] Add required dependency license files to binary distributions.
- [ ] Search the repository for private paths, usernames, email addresses, credentials, and local test filenames.
- [ ] Build from a fresh clone using `build/BuildRelease.ps1`.
- [ ] Test the installer on a clean Windows x64 user account or VM.
- [ ] Verify the installer blocks cleanly when the x64 .NET 10 Runtime is missing.
- [ ] Verify install and COM registration after installing the x64 .NET 10 Runtime.
- [ ] Test install, upgrade, repair/reinstall, and uninstall.
- [ ] Test `.glb` and `.gltf` thumbnails after clearing the Windows thumbnail cache.
- [ ] Confirm no PDB, test model, diagnostic log, or manual test image is included in the installer.
- [ ] Add screenshots and a release changelog.
- [ ] Create a Git tag matching the installer version.
