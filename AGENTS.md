# Repository Guidelines

## Project Structure

- `Runtime/`: runtime C# code plus shaders and compute kernels.
  - `Runtime/Shaders/`: `Gsplat.shader`, `Gsplat.compute`, `GsplatVfx.compute`.
  - `Runtime/SRP/`: URP/HDRP integration.
  - `Runtime/VFX/`: optional VFX Graph backend (guarded by defines).
- `Editor/`: editor-only code (importers, inspectors, settings UI).
- `Samples~/`: Package Manager samples (see "Gsplat > Samples").
- `Documentation~/`: implementation notes and images.
- `Tools~/`: offline utilities (Unity ignores `~` folders), e.g. `.splat4d` generators.
- `openspec/`: archived specs/changes used to track design and tasks.

## Build, Test, and Development Commands

This is a Unity UPM package. Iterate by installing it into a Unity project:

- Unity: `Window > Package Manager > + > Install package from disk...` and select `package.json`.
- Offline `.splat4d` conversion tool:

```bash
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --help
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --input-dir /path/to/time_*.ply --output out.splat4d --mode average
```

## Coding Style & Naming Conventions

- C#: 4-space indentation, braces on new lines, `PascalCase` for public APIs, `m_` prefix for private fields.
- Keep runtime/editor separation: runtime code stays in `Runtime/` (`Gsplat.asmdef`), editor-only code stays in `Editor/` (`Gsplat.Editor.asmdef`).
- Optional packages are compile-guarded via asmdef version defines: `GSPLAT_ENABLE_URP`, `GSPLAT_ENABLE_HDRP`, `GSPLAT_ENABLE_VFX_GRAPH`.

## Testing Guidelines

- EditMode tests live in `Tests/Editor/` (Unity Test Framework/NUnit). In Test Runner, enable package tests and run `Gsplat.Tests.Editor`.
- Treat `Samples~/` as the primary visual smoke test.
- Note: Package Manager samples are copied into the Unity project under `Assets/Samples/...` and do not auto-update when `Samples~/` changes; if a sample fix "doesn't work", check whether you're running the copied sample instead of the package source.
- When changing sorting/shaders, validate on a subgroup-capable graphics API (D3D12, Metal, or Vulkan).
- Metal compute caveat: Metal does not support buffer `GetDimensions` queries in HLSLcc. Pass buffer sizes as constants from C# when needed.
- When debugging `Kernel at index (...) is invalid`, treat it as "kernel compile failed" as well as "kernel missing". Prefer `ComputeShader.IsSupported` for a stable "can this run" check; `GetKernelThreadGroupSizes` is useful but can throw on some Unity/Metal combinations.
- For RGBA8 UNorm "data textures" (e.g. `TextureFormat.RGBA32`), prefer reading as `float4` and converting to bytes in HLSL; integer views can be stricter on Metal.
- When touching VFX backend code, verify compilation both with and without `com.unity.visualeffectgraph` installed.

## Commit & Pull Request Guidelines

- Commit messages are short and imperative. Examples from history: `fix: ...`, `refactor ...`, `Supports ... (#6)`, and release commits like `v1.1.2`.
- PRs should include: Unity version, render pipeline (BiRP/URP/HDRP), platform + graphics API, repro steps, and a screenshot/video for visual changes.
- Update `CHANGELOG.md` for user-visible behavior changes. For releases, also bump `package.json` version (SemVer).
