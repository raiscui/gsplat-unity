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
  - Editor (Edit Mode): SceneView stays visible and stays sorted even while you interact with other Editor UI (Inspector/Hierarchy).
    - Sorting is driven by the SceneView camera.
    - Rendering is submitted to SceneView cameras to avoid "visible/invisible" flicker caused by unstable Editor camera instances.
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
