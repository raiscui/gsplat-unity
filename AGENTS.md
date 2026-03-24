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
- In minimal repro projects, package tests are not compiled or runnable unless `Packages/manifest.json` includes `"testables": ["wu.yize.gsplat"]`.
- When changing a `ScriptedImporter` output shape (for example main object type, prefab generation, sub-assets, or default renderer binding), bump the `[ScriptedImporter(version, ...)]` number and update importer tests; otherwise existing imported assets can stay cached in the old shape and make the fix look ineffective.
- Treat `Samples~/` as the primary visual smoke test.
- When running EditMode tests via Unity CLI (`-runTests`), if you see the Editor exit but no `-testResults` XML is generated, try removing `-quit` or moving `-quit` to the end of the argument list (some Editor versions can quit before the TestRunner starts when `-quit` is placed early).
- After `refresh_unity` or any domain reload, do not treat a readable `mcpforunity://editor/state` as proof that Unity MCP actions are healthy. Cross-check `~/.unity-mcp/unity-mcp-status-*.json`, the actual listener port via `lsof`, and only accept test jobs with a non-zero total count; `summary.total = 0` is not a valid pass.
- Headless Unity CLI runs (`-batchmode -nographics`) use `GraphicsDeviceType.Null`; guard graphics-only initialization (for example sorter setup or VFX kernel discovery) or you can get misleading `Kernel '...' not found` logs even when importer/runtime logic is otherwise correct.
- Do not use headless runs as final visual proof. In Edit Mode, Unity MCP `manage_camera screenshot` can also miss the actual on-screen GameView and return blank images; for final display verification, prefer an on-screen Unity window/GameView capture or a purpose-built verifier scene.
- Note: Package Manager samples are copied into the Unity project under `Assets/Samples/...` and do not auto-update when `Samples~/` changes; if a sample fix "doesn't work", check whether you're running the copied sample instead of the package source.
- If you changed shaders/C# but see **zero** runtime difference, first verify you are editing the copy Unity actually uses (manifest/local path vs embedded). For fast proof in Editor, log the `AssetDatabase` path of the shader/material used at the draw submission point.
- When changing sorting/shaders, validate on a subgroup-capable graphics API (D3D12, Metal, or Vulkan).
- For LiDAR features that derive angle/layout data from a camera, keep layout LUT generation, runtime sensor context, and point-cloud reconstruction on the same rigid sensor frame. Do not let parent scale leak in via raw `transform.InverseTransformPoint/Direction`, or you can get curved near-plane geometry and mismatched rays.
- For large linear compute workloads, do not assume one `DispatchCompute(groupsX, 1, 1)` can scale arbitrarily. If item counts can approach the per-dimension group limit, chunk on the CPU and pass a base index into the shader.
- Historical note: `LidarBeamCount` no longer has a runtime hard clamp of `512`. If older notes mention that clamp, treat them as historical context only; the live limit is now performance and memory cost from `beamCount * azimuthBins`.
- For overlap/timeline handoff features, do not directly reuse public APIs that also perform `Cancel*`, `Reset*`, or `Clear*` side effects. Prefer extracting a side-effect-free internal transition helper, then let the public API keep its reset semantics for normal one-shot use.
- Metal compute caveat: Metal does not support buffer `GetDimensions` queries in HLSLcc. Pass buffer sizes as constants from C# when needed.
- Metal render caveat: if you see `requires a ComputeBuffer at index (...) ... Skipping draw calls to avoid crashing`, treat it as "a StructuredBuffer binding is missing". Prefer rebinding all StructuredBuffers right before each draw call, and for optional buffers (e.g. 4D buffers) bind a dummy buffer even when the feature is off (Metal can still skip draws if the shader declares the buffer but nothing is bound).
- When debugging `Kernel at index (...) is invalid`, treat it as "kernel compile failed" as well as "kernel missing". Prefer `ComputeShader.IsSupported` for a stable "can this run" check; `GetKernelThreadGroupSizes` is useful but can throw on some Unity/Metal combinations.
- For RGBA8 UNorm "data textures" (e.g. `TextureFormat.RGBA32`), prefer reading as `float4` and converting to bytes in HLSL; integer views can be stricter on Metal.
- When touching VFX backend code, verify compilation both with and without `com.unity.visualeffectgraph` installed.
- Editor SRP caveat: in Edit Mode, Unity can call `RenderPipelineManager.beginCameraRendering` multiple times within the same `Time.frameCount`. If draw submission happens only once per `ExecuteAlways.Update`, you can get `render invocation count > draw submission count` flicker. Align draw submission to the camera callbacks and do not filter SceneView cameras by `isActiveAndEnabled` (SceneView internal cameras can be disabled but still participate in SRP callbacks).

## Tooling Notes

- When appending Markdown/context files from shell and the body contains backticks, use a single-quoted heredoc like `cat <<'EOF'`. Unquoted heredocs or double-quoted wrapper strings can execute backticked text via shell expansion and corrupt the note.
- Repo-local Codex skills must keep the `SKILL.md` front-matter `name` at 64 characters or fewer, or Codex will skip loading the skill as invalid.

## Long-Term Knowledge

- `EXPERIENCE.md`: 项目级经验沉淀。做 continuous-learning、回读旧支线、排查 Unity MCP 验证异常,或继续 LiDAR compute / 分辨率问题前,优先先读这里。

## Commit & Pull Request Guidelines

- Commit messages are short and imperative. Examples from history: `fix: ...`, `refactor ...`, `Supports ... (#6)`, and release commits like `v1.1.2`.
- PRs should include: Unity version, render pipeline (BiRP/URP/HDRP), platform + graphics API, repro steps, and a screenshot/video for visual changes.
- Update `CHANGELOG.md` for user-visible behavior changes. For releases, also bump `package.json` version (SemVer).
