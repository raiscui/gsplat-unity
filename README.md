# Gsplat

[![Changelog](https://img.shields.io/badge/changelog-f15d30.svg)](./CHANGELOG.md) [![Version](https://img.shields.io/badge/version-v1.1.4-blue.svg)](./CHANGELOG.md) [![License](https://img.shields.io/badge/license-MIT-green.svg)](./LICENSE.md)

A Unity package for rendering [3D Gaussian Splatting](https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/) (3DGS). Supports Unity 2021 and later. 

![lego](Documentation~/Images/lego.png)

The 3DGS rendering pipeline design of this package is inspired by [PlayCanvas](https://github.com/playcanvas/engine), which treats 3DGS objects similarly to transparent meshes that use a custom shader. With this approach, only an additional sorting pass needs to be inserted into each camera's command buffer. This design makes it easier to integrate 3DGS rendering into an existing pipeline, allows the draw calls for 3DGS objects to be correctly inserted into the existing rendering queue for transparent meshes (based on their bounding boxes), rather than rendering all 3DGS objects to a separate render texture as is done in [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). 

That sounds great, but at what cost?

Most 3DGS assets are trained in Gamma space, following the official implementation. This means that the alpha blending for the Gaussians is also performed in Gamma space.  Since there is no longer an additional render texture that would allow us to convert the color space after the alpha blending of 3DGS, you must ensure your project's color space (`Edit > Project Settings > Player > Other Settings > Rendering > Color Space`) is set to "Gamma" for the 3DGS assets to be rendered correctly (be aware that HDRP doesn't support Gamma mode). For projects using a linear color space, you must retrain the 3DGS asset with linear-space images. While this plugin offers a `Gamma To Linear` option as a workaround, converting the color space before alpha blending leads to incorrect results and will lower the 3DGS rendering quality.

## Highlights

- Supports Built-in Render Pipeline (BiRP), URP and HDRP

- Gaussians can be correctly blended with transparent meshes based on their bounding boxes

- Supports reading & rendering PLY files with SH degrees 0-3

- Supports optional 4DGS fields (velocity/time/duration) with a TimeNormalized playback control

- Supports importing `.splat4d` binary assets (64 bytes/record, SH0 only)

- Supports importing `.sog4d` keyframe sequence bundles (SOG-style ZIP bundle, SH0-3, interpolable per-frame streams)

- Supports orthographic projection

- Compatible with MSAA

- Compatible with XR

  - | XR Render Mode        | BiRP | URP  | HDRP |
    | --------------------- | ---- | ---- | ---- |
    | Multi-pass            | ✓    | ✓    | ✗    |
    | Single Pass Instanced | ✗    | ✓    | ✗    |

## Platform Compatibility

The sorting pass, built upon [b0nes164/GPUSorting](https://github.com/b0nes164/GPUSorting), requires wave / subgroups operations which are only available in D3D12, Metal or Vulkan graphics APIs. WebGPU supports the subgroup operations but Unity has not implemented it. Anything using other graphics APIs will not work. I have only tested on Windows, Mac and Android, but the compatibility of this package should be similar to [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting).

## Usage

### Install

After cloning or downloading this repository, open your Unity project (or create a new one). Navigate to `Window > Package Manager`, click the `+` button, select `Install package from disk...`, and then choose the `package.json` file from this repository.

### Setup

First, ensure your project is using a supported Graphics API. For Windows: in `Edit > Project Settings > Player > Other Settings`, uncheck `Auto Graphics API for Windows`. Then, in the `Graphics APIs for Windows` list, add `Vulkan` or `Direct3D12` and remove any other options. Unity will require a restart to switch the Graphics API. You may need to perform similar steps for other platforms. 

Note that for Android, you also need to uncheck `Apply display rotation during rendering` in `Player > Settings for Android > Other Settings > Vulkan Settings`, as this package currently does not support rendering in the native display orientation.

The next steps depend on the Render Pipeline you are using:

- BiRP: Does not need any extra setup.
- URP/HDRP: No extra setup is required.
  - Sorting is automatically dispatched per-camera via SRP callbacks, so both GameView and SceneView cameras stay in sync.
  - If you previously added `Gsplat URP Feature` or a `Gsplat HDRP Pass` CustomPass, you can keep them; they will auto-no-op to avoid duplicate sorting.

Camera selection note:

- By default, `GsplatSettings.CameraMode` is `ActiveCameraOnly` (performance mode).
  - Play Mode / Player: only one active Game/VR camera is sorted and rendered each frame (prefers `Camera.main`).
  - Editor (Edit Mode): camera selection uses a sticky "last interacted viewport" hint.
    - Interact with SceneView: SceneView is treated as active (stable while you use other Editor UI like Inspector/Hierarchy).
    - Interact with GameView: the active Game/VR camera is treated as active (and stays active while Inspector takes focus, e.g. when dragging `TimeNormalized`).
    - Rendering is submitted from camera callbacks to avoid "visible/invisible" flicker caused by unstable Editor camera instances.
  - If `GsplatSorter.Instance.ActiveGameCameraOverride` is set to a valid camera, it always takes precedence in Play Mode / Player.
    - Convenience: add `GsplatActiveCameraOverride` to your main Game/VR camera to manage this automatically.
      - When multiple overrides exist, higher `Priority` wins; ties prefer the last enabled one.
- If you need every camera (reflection / portals / probes) to see Gsplat, set `CameraMode = AllCameras` in `Project Settings > Gsplat`.

Diagnostics note (Editor):

- If splats occasionally disappear or flicker in the Editor (especially on macOS/Metal), enable `EnableEditorDiagnostics` in `Project Settings > Gsplat`.
  - When a Metal draw-skip warning is detected, Gsplat will auto-dump a `[GsplatDiag]` block to the Console / `Editor.log` with recent camera/sort/draw events and shader buffer indices.
  - You can also manually dump it via `Tools > Gsplat > Dump Editor Diagnostics`.

### Import Assets

Copy or drag & drop the PLY file anywhere into your project's `Assets` folder. The package will then automatically read the file and import it as a `Gsplat Asset`.

This package also supports:

- 4D PLY files that include FreeTimeGS-style fields (`vx/vy/vz`, `time`, `duration`).
- `.splat4d` binary files (see format below).
- `.sog4d` keyframe sequence bundles (see format below).

### Add Gsplat Renderer

Create or choose a game object in your scene, and add the `Gsplat Renderer` component on it. Point the `Gsplat Asset` field to one of your imported Gsplat Assets. Then it should appear in the viewport.

The `SH Degree` option sets the order of SH coefficients used for rendering. The final value is capped by the Gsplat Asset's `SH Bands`.

### Render style (Gaussian vs ParticleDots)

Both `Gsplat Renderer` and `GsplatSequenceRenderer` support two render styles:

- `Gaussian` (default): standard elliptical Gaussian splats.
- `ParticleDots`: screen-space solid discs ("particle dots") with a soft edge.

Dot size is controlled by `ParticleDotRadiusPixels` (radius in screen pixels).

You can switch styles via API. The default transition is an animated shader morph using `easeInOutQuart` over `1.5s`:

```csharp
var r = GetComponent<GsplatRenderer>();
r.SetRenderStyle(GsplatRenderStyle.ParticleDots); // animated, 1.5s easeInOutQuart
r.SetParticleDotRadiusPixels(6.0f);

r.SetRenderStyle(GsplatRenderStyle.Gaussian, animated: false); // hard switch
r.SetRenderStyle(GsplatRenderStyle.Gaussian, animated: true, durationSeconds: 0.3f); // custom duration
```

### LiDAR scan visualization (experimental)

Both `Gsplat Renderer` and `GsplatSequenceRenderer` include an optional "car-like LiDAR scan" visualization mode.

Instead of rendering the hit splats directly, it renders a **regular point grid** (beam x azimuthBins), while using the splats as the sampling targets ("environment points"). This makes the scan lines look tidy and gives proper **first return** occlusion semantics.

Enable it in the Inspector:

- `EnableLidarScan = true`
- `LidarApertureMode`
  - `Surround360`: keeps the legacy 360-degree scan semantics, and still uses `LidarOrigin` as the sensor pose.
  - `CameraFrustum`: uses `LidarFrustumCamera` as the authoritative sensor-frame.
    - Camera position = LiDAR origin
    - Camera rotation = LiDAR orientation
    - Camera projection / aspect / pixelRect = LiDAR aperture and cell-to-screen mapping
- `LidarOrigin` = only required for `Surround360`
- `LidarFrustumCamera` = only required for `CameraFrustum`
- Optional external mesh inputs:
  - `LidarExternalStaticTargets` = static scan roots
    - Recursively collects child `MeshRenderer + MeshFilter` and `SkinnedMeshRenderer`
    - In `CameraFrustum` mode, static targets use the GPU capture path and are only recaptured when their signature changes:
      - frustum camera pose / projection / aspect / pixelRect
      - capture RT layout / LiDAR cell mapping
      - renderer active/enabled state
      - transform / mesh / material main color (`_BaseColor`, fallback `_Color`)
  - `LidarExternalDynamicTargets` = dynamic scan roots
    - Designed for moving props, animated meshes, and `SkinnedMeshRenderer`
    - In `CameraFrustum` mode, dynamic targets use a separate capture cache and refresh at `LidarExternalDynamicUpdateHz`
  - Legacy `LidarExternalTargets` is still accepted and maps to `LidarExternalStaticTargets` for backward compatibility
  - `LidarExternalTargetVisibilityMode = ForceRenderingOff` by default, so those targets can stay scan-only (participate in LiDAR, but stop rendering as ordinary meshes)
  - `ForceRenderingOffInPlayMode` keeps the original mesh visible while editing, but automatically hides it during Play mode
  - Switch to `KeepVisible` if you want the original mesh rendering to remain visible alongside the LiDAR point cloud
- Optional: `HideSplatsWhenLidarEnabled = true` to render only the LiDAR point cloud (no splats)

External targets share the same **first return** semantics with gsplat:

- Each `(beam, azimuthBin)` compares the gsplat hit and the external-mesh hit, and only the nearest result survives.
- This means external targets can occlude gsplat, and gsplat can still win when it is closer.
- `Depth` mode keeps using the same depth gradient for both hit sources.
- `SplatColorSH0` keeps using SH0 base color for gsplat hits, while external hits use the hit material main color (`_BaseColor`, fallback `_Color`, otherwise white).
- In `CameraFrustum` mode, external mesh capture uses an explicit render list + override material + command buffer draw path, so the scan no longer depends on whether the source renderer is visible in the scene.
- The frustum GPU resolve converts captured view-depth back into LiDAR ray-distance / `depthSq` semantics before writing the external hit buffer. It does not compare raw hardware depth against the LiDAR range image.
- `Surround360`, unsupported GPU-capture platforms, or missing capture resources still fall back to the legacy CPU `RaycastCommand` route.
- The LiDAR show/hide coverage radius now uses the combined bounds of gsplat + external targets, so external meshes outside the original gsplat bounds still participate in the reveal/burn radius.
- `LidarExternalHitBiasMeters` can push external-hit particles slightly forward along the sensor ray at render time, which helps keep RadarScan points from sitting just behind the visible source mesh. This is a render-only bias: it does not change first-return competition, stored hit distance, or depth-color evaluation. The default is `0` (off), so only opt in when you actually need the extra forward push.

Default scanning setup (as discussed in the spec):

- Grid: `LidarBeamCount = 128` beams x `2048 azimuth bins`
- Update strategy: `LidarUpdateHz = 10` (full 360 range image rebuild every 0.1s)
- Scan head visualization: `LidarRotationHz = 5` (brightness front + 1-revolution afterglow)
- Color modes:
  - `Depth` with `LidarDepthNear = 1m`, `LidarDepthFar = 200m` (cyan -> blue -> purple -> red depth gradient)
  - `SplatColorSH0` (samples the hit splat base color from SH0)
- Depth opacity:
  - `LidarDepthOpacity` (0..1, default `1`, only affects `Depth` mode)
- Point size: `LidarPointRadiusPixels` (default `2px` radius)
- Particle antialiasing:
  - `LidarParticleAntialiasingMode`
  - `LidarParticleAAFringePixels`
  - `LegacySoftEdge` = compatibility default, keeps the old fixed-feather edge look
  - `AnalyticCoverage` = recommended general mode, uses derivative-driven local coverage AA in pixel-space, and non-legacy AA modes now reserve a configurable outer fringe around each point so small LiDAR points show a clearer edge difference without requiring MSAA
  - `AlphaToCoverage` / `AnalyticCoveragePlusAlphaToCoverage` = require effective MSAA on the actual render camera; these modes now use a coverage-first pass instead of the regular alpha-blended LiDAR shell, and reuse the same outer fringe space so the edge looks more like sample coverage / cutout than soft transparent blending
  - In HDRP, A2C availability follows the camera's resolved HD Frame Settings / MSAA mode instead of `Camera.allowMSAA`, because HDRP treats that Camera flag as a legacy path
  - If MSAA is unavailable, runtime falls back to `AnalyticCoverage`
  - `LidarParticleAAFringePixels` controls how much outer fringe space non-legacy AA modes get. `0` means no extra outward fringe. `1` is the current default baseline.
- Rendering: screen-space square points
  - `LegacySoftEdge` / `AnalyticCoverage` use the original alpha-blended LiDAR pass
  - A2C modes use a coverage-first pass on MSAA targets
  - Scan head + trail: `LidarTrailGamma`, `LidarIntensity`
  - Optional base intensity (prevents "black after sweep"): `LidarKeepUnscannedPoints`, `LidarUnscannedIntensity`
  - Optional distance attenuation (near stronger, far weaker):
    - `LidarIntensityDistanceDecayMode` (`Reciprocal` / `Exponential`)
    - `LidarIntensityDistanceDecay`, `LidarUnscannedIntensityDistanceDecay` (0 disables)
- Noise filter: `LidarMinSplatOpacity` (default `1/255`, filters near-invisible splats to avoid a "transparent shell" look)

API example:

```csharp
var r = GetComponent<GsplatRenderer>();
r.EnableLidarScan = true;
r.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
r.LidarFrustumCamera = lidarCamera;
r.LidarExternalStaticTargets = new[] { roadSignsRoot, roadSurfaceRoot };
r.LidarExternalDynamicTargets = new[] { carRoot, characterRoot };
r.LidarExternalDynamicUpdateHz = 15.0f;
r.LidarExternalTargetVisibilityMode = GsplatLidarExternalTargetVisibilityMode.ForceRenderingOffInPlayMode;
r.LidarExternalHitBiasMeters = 0.0f; // keep at 0 by default, opt in only if points look slightly behind the mesh
r.HideSplatsWhenLidarEnabled = true;
r.LidarColorMode = GsplatLidarColorMode.Depth;
r.LidarDepthOpacity = 1.0f;
r.LidarPointRadiusPixels = 2.0f;
r.LidarParticleAntialiasingMode = GsplatLidarParticleAntialiasingMode.AnalyticCoverage;
r.LidarParticleAAFringePixels = 1.0f;
r.LidarKeepUnscannedPoints = true;
r.LidarUnscannedIntensity = 0.2f;
r.LidarIntensityDistanceDecayMode = GsplatLidarDistanceDecayMode.Exponential;
r.LidarIntensityDistanceDecay = 0.02f;
r.LidarUnscannedIntensityDistanceDecay = 0.02f;
```

Manual verification checklist:

- In `Surround360` mode, the 360-degree scan head rotates at ~`LidarRotationHz` (default 5Hz)
- Scan head rotates and leaves a 1-revolution trail (`LidarTrailGamma`, `LidarIntensity`)
- In `CameraFrustum` mode, the scan aperture follows `LidarFrustumCamera` instead of `LidarOrigin`
- When `LidarKeepUnscannedPoints=true`, points do not fade to black before the next sweep (uses `LidarUnscannedIntensity`)
- When `Lidar*DistanceDecay > 0`, intensity attenuates with distance (near stronger, far weaker)
- `Depth` / `SplatColorSH0` switching works
- With `LidarExternalStaticTargets` / `LidarExternalDynamicTargets` configured, external meshes participate in the same first-return competition as gsplat
- In `CameraFrustum` mode, static external meshes do not recapture every LiDAR tick when only gsplat data updates
- In `CameraFrustum` mode, dynamic external meshes refresh at `LidarExternalDynamicUpdateHz` and can remain stale between refreshes by design
- With `LidarExternalTargetVisibilityMode=ForceRenderingOff`, external targets disappear as ordinary meshes but still remain valid LiDAR scan targets
- With `LidarExternalTargetVisibilityMode=ForceRenderingOffInPlayMode`, external targets remain visible while editing but switch to scan-only during Play mode
- With ordinary meshes still visible, `LidarExternalHitBiasMeters` can stay at `0` by default, and only be increased slightly (`0.01`, `0.02`, ...) if the RadarScan particles look like they are sitting just behind the source mesh surface
- In `SplatColorSH0`, external hits use the hit material main color instead of SH0
- `LidarParticleAntialiasingMode=AnalyticCoverage` now computes coverage in pixel-space and uses `LidarParticleAAFringePixels` as the outer fringe width, so small points should show a more obvious edge difference than the legacy fixed-feather path
- `LidarParticleAntialiasingMode=AlphaToCoverage` or `AnalyticCoveragePlusAlphaToCoverage` only stays active when the actual render camera has MSAA; otherwise runtime falls back to `AnalyticCoverage`
- When A2C is active, the LiDAR A2C shell uses a coverage-first pass instead of ordinary alpha blending, so expect a more cutout/sample-coverage style edge
- `SkinnedMeshRenderer` targets update after pose changes on the next `LidarExternalDynamicUpdateHz` capture tick (or the next CPU fallback scan tick)
- `HideSplatsWhenLidarEnabled=true` stops splat sort/draw while LiDAR still works

### 4DGS motion model and visibility

When a `Gsplat Asset` contains 4D fields (`Velocities/Times/Durations`), splat centers are evaluated at time `t`:

`pos(t) = pos0 + vel * (t - time0)`

Visibility is gated by the time window:

- Visible only when `t` is in `[time0, time0 + duration]`
- Fully invisible outside the window (no color contribution)

`time0` and `duration` are treated as normalized values in `[0, 1]`.

### 4D PLY field names

The PLY importer recognizes these vertex properties:

- Velocity: `vx`, `vy`, `vz` (aliases: `velocity_x`, `velocity_y`, `velocity_z`)
- Time: `time` (alias: `t`)
- Duration: `duration` (alias: `dt`)

If none of the 4D fields are present, the asset is treated as 3D-only and rendering is unchanged.

### `.splat4d` binary format (v1)

`.splat4d` is a headerless array of fixed-size records:

- Record size: 64 bytes
- Endianness: little-endian
- File size must be a multiple of 64 bytes

Record layout (byte offsets):

- `0..11`: `px/py/pz` (float32 * 3)
- `12..23`: `sx/sy/sz` (float32 * 3, **linear scale**, not log-scale)
- `24..27`: `r/g/b/a` (uint8 * 4)
  - `rgb` is base color: `baseRgb = f_dc * SH_C0 + 0.5` quantized to `[0,255]`
  - `a` is opacity in `[0,1]`
- `28..31`: `rw/rx/ry/rz` (uint8 * 4, quantized quaternion)
  - `v = (byte - 128) / 128`, stored as `(w, x, y, z)`
- `32..43`: `vx/vy/vz` (float32 * 3)
- `44..47`: `time` (float32, normalized)
- `48..51`: `duration` (float32, normalized)
- `52..63`: padding (reserved)

### `.sog4d` keyframe sequence bundle (v1)

`.sog4d` is a single-file ZIP bundle designed for **per-frame keyframes** with **interpolation** over:
`position`, `scale`, `rotation`, `opacity`, and `SH (0-3)`.

At the root of the ZIP, there must be a `meta.json` that defines:
- `splatCount`, `frameCount`
- `timeMapping` (`uniform` or `explicit`)
- `layout` (row-major `width/height`, mapping `splatId -> pixel`)
- `streams` (per-frame data textures + codebooks/palettes)

When importing a `.sog4d` into Unity, the package:
- Decodes the WebP data textures into `Texture2DArray` sub-assets.
- Creates a playable prefab (main object) with a `GsplatSequenceRenderer` component.
- Evaluates `(i0, i1, a)` from `TimeNormalized`, then runs a compute decode+interpolate pass to write float buffers.
  - Sorting and rendering reuse the existing Gsplat pipeline.

Player build runtime loading (keep the bundle compressed):
- You can load a `.sog4d` ZIP bundle at runtime via `GsplatSequenceRenderer`:
  - `RuntimeSog4dPath`: reads bytes from a file path (typically under `StreamingAssets`).
  - `RuntimeSog4dBundle`: reads bytes from a `TextAsset`.
- Enable `RuntimeEnableChunkStreaming` to load only a frame chunk (with 1-frame overlap) on demand, reducing VRAM peak for long sequences.

Offline exporter (PLY sequence -> `.sog4d`):

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out.sog4d \
  --time-mapping uniform \
  --shN-count 8192 \
  --shN-labels-encoding delta-v1 \
  --delta-segment-length 50 \
  --self-check
```

Common import errors and fixes:
- `Unity ImageConversion.LoadImage returned false`: your Unity version may not support WebP decoding. On macOS Editor, the package falls back to an embedded `libwebp` decoder; if it still fails, make sure `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib` is present and imported.
- `HLSLcc: Metal shading language does not support buffer size query from shader`: the sequence decode compute shader failed to compile on Metal (usually due to querying buffer sizes in shader code). Update to a version that passes required buffer counts from C# instead.
- `GsplatSequenceDecode.compute: Kernel at index (...) is invalid`: the sequence decode compute kernel likely failed to compile on the current Graphics API. Make sure `GsplatSequenceRenderer.DecodeComputeShader` points to `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSequenceDecode.compute`. If you're running headless (`-batchmode -nographics`), sequence playback is disabled by design.
- `ScaleIndices u16 index out of range`: `scale_indices.webp` contains an index >= `streams.scale.codebook.Length`.
- `shN base labels out of range`: base labels contain a label >= `streams.sh.shNCount` (palette size).

Notes:

- `.splat4d` does **not** apply implicit axis flips during import.
- `.splat4d` v1 is **SH0-only** (`SH Bands = 0`). Use PLY if you need higher-order SH.

### Playback controls (TimeNormalized)

`Gsplat Renderer` exposes:

- `TimeNormalized` (0..1)
- `AutoPlay`, `Speed` (normalized time / second), `Loop`

Sorting (compute) and rendering (shader) use the same cached `TimeNormalized` value per frame.

Editor note:

- If `GsplatSettings.CameraMode = ActiveCameraOnly` (default):
  - In Play Mode, only the active Game/VR camera sorts and renders.
  - SceneView focus will not trigger an extra sort, so the "double sorting" issue is avoided by design.
- If you switch to `AllCameras`:
  - Play Mode often renders both the GameView and the SceneView.
  - Sorting may run multiple times per frame (once per camera) and reduce FPS.
  - Use these Editor-only toggles to prioritize GameView performance:
    - `GsplatSettings.SkipSceneViewSortingInPlayMode` (default: `true`)
    - `GsplatSettings.AllowSceneViewSortingWhenFocusedInPlayMode` (default: `true`)
    - `GsplatSettings.SkipSceneViewRenderingInPlayMode` (default: `true`)

### VFX Graph backend (optional)

If your project includes the Visual Effect Graph package (`com.unity.visualeffectgraph`), this package provides:

- `GsplatVfxBinder` (binds GPU buffers to a `VisualEffect`)
- A minimal VFX Graph sample (see Package Manager > Gsplat > Samples)

Important limitations:

- The VFX backend enforces a hard limit: `GsplatSettings.MaxSplatsForVfx` (default 500k).
- When the splat buffer capacity exceeds the limit, `GsplatVfxBinder` auto-disables the `VisualEffect` and logs a warning.
- To avoid double rendering when using the VFX backend, you can set `GsplatRenderer.EnableGsplatBackend = false`.

The `Gamma To Linear` option is offered as a workaround to render Gamma Space Gsplat Assets in a project using the Linear Space. This will degrade the rendering quality, so changing the color space of the project or retraining the 3DGS asset is the recommended approach. If your project uses a linear color space and you do not wish to retrain your 3DGS assets, it is recommended to use [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting).

## Additional Documentation

- [Implementation Details](./Documentation~/Implementation%20Details.md)

## Project Using Gsplat

- [HiFi-Human/DynGsplat-unity](https://github.com/HiFi-Human/DynGsplat-unity) - A Unity package for rendering and playing dynamic gaussian splatting sequences

## License

This project is released under the MIT license. It is built upon several other open-source projects:

-   [playcanvas/engine](https://github.com/playcanvas/engine), MIT License (c) 2011-2024 PlayCanvas Ltd
-   [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting), MIT License (c) 2023 Aras Pranckevičius
-   [b0nes164/GPUSorting](https://github.com/b0nes164/GPUSorting), MIT License (c) 2024 Thomas Smith
