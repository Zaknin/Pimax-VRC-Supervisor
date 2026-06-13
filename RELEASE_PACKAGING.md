# Release Packaging Layout

Use `scripts/package-release.ps1` to build the final flat Windows release folders and zip files.

## Zip Variants

Create both release variants when requested:

- `PimaxVrcSupervisor-v1.3.0-win-x64-with-dotnet9.zip`: full self-contained release for users who do not have .NET 9 installed.
- `PimaxVrcSupervisor-v1.3.0-win-x64-no-dotnet9.zip`: smaller release for users who already have the .NET 9 Windows Desktop Runtime x64 installed.

The `no-dotnet9` package requires the Windows Desktop Runtime, not only the base .NET runtime, because the supervisor and configurator use Windows Forms.

## Flat Folder Shape

Keep executables, DLLs, config, README, and release notes in the same folder. Do not create an `app\` subfolder or root shortcuts for this layout.

```text
PimaxVrcSupervisor-vX.Y.Z\
  Assets\
    vr-overlay-icon.png

  PimaxVrcSupervisor.exe
  PimaxVrcSupervisor.dll
  PimaxVrcSupervisor.deps.json
  PimaxVrcSupervisor.runtimeconfig.json

  PimaxVrcSupervisorConfigurator.exe
  PimaxVrcSupervisorConfigurator.dll
  PimaxVrcSupervisorConfigurator.deps.json
  PimaxVrcSupervisorConfigurator.runtimeconfig.json

  PimaxVrcSupervisorSteamVrHost.exe
  PimaxVrcSupervisorSteamVrHost.dll
  PimaxVrcSupervisorSteamVrHost.deps.json
  PimaxVrcSupervisorSteamVrHost.runtimeconfig.json

  PimaxVrcSupervisorTui.exe

  PimaxVrcSupervisorStartupHelper.exe
  PimaxVrcSupervisorWatcher.exe

  SharpGen.Runtime.dll
  SharpGen.Runtime.COM.dll
  Vortice.Direct2D1.dll
  Vortice.Direct3D11.dll
  Vortice.DirectX.dll
  Vortice.DXGI.dll
  Vortice.Mathematics.dll

  supervisor.config.json
  README.md
  RELEASE_NOTES.md
```

## Full Release: No .NET 9 Installed

For the full self-contained release, also keep all bundled .NET/Windows Desktop runtime files produced by publish, including:

```text
Accessibility.dll
hostfxr.dll
hostpolicy.dll
coreclr.dll
clrjit.dll
clrgc.dll
clrgcexp.dll
clretwrc.dll
createdump.exe
Microsoft*.dll
mscordaccore*.dll
mscordbi.dll
mscorlib.dll
mscorrc.dll
msquic.dll
netstandard.dll
System*.dll
WindowsBase.dll
WinRT.Runtime.dll
```

Do not hand-pick individual `System.*` or `Microsoft.*` runtime assemblies unless the build process has an automated dependency check. For the full release, the safe rule is to keep the self-contained publish runtime DLLs and remove only the explicit exclusions below.

## No-Dotnet9 Release: .NET 9 Already Installed

For the `no-dotnet9` release, do not include bundled .NET runtime files. Keep the framework-dependent app files produced by publish, including `.deps.json`, `.runtimeconfig.json`, application DLLs, and third-party DLLs.

Users of this zip must install:

```text
.NET 9 Windows Desktop Runtime x64
```

## Cleanup Rules

Always remove or exclude:

```text
*.pdb
*.bak
supervisor.active-config.txt
supervisor_moved.config.json
supervisor_moved.config.*.bak
```

Remove or exclude satellite language folders:

```text
cs\
de\
es\
fr\
it\
ja\
ko\
pl\
pt-BR\
ru\
tr\
zh-Hans\
zh-Hant\
```

Do not include:

```text
Assets\app.ico
Assets\config-editor.ico
```

Keep:

```text
Assets\vr-overlay-icon.png
```

The `.ico` files are embedded into the executables at build time and are not needed at runtime. The SteamVR overlay icon PNG is loaded from disk and must stay in `Assets\` beside the executables.

The language folders are .NET/Windows Forms satellite resources. They are not required while the app is English-only and no interface/software translation is planned.

## Rust Terminal UI Packaging

Include the Terminal UI executable:

```text
PimaxVrcSupervisorTui.exe
```

Build it with the Rust stable MSVC toolchain:

```powershell
cargo build --manifest-path .\PimaxVrcSupervisor.Tui\Cargo.toml --release
```

The packaging script builds this binary and copies it into both release variants.

```powershell
.\scripts\package-release.ps1
```

Generated output rules:

- `PimaxVrcSupervisor.Tui\target\` is generated Rust build output and must not be committed.
- `release\` is generated local release output and must not be committed.
- `PimaxVrcSupervisor.Tui\Cargo.lock` should be committed because the TUI is an application binary crate.
