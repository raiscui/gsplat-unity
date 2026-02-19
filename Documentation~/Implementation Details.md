## Implementation Details

### Resources Setup

**Material & Mesh**: The `GsplatSettings` singleton creates a set of materials from the `Gsplat.shader`, one for each SH degree (0-3). It also procedurally generates a `Mesh` that consists of multiple quads. The number of quads is defined by `SplatInstanceSize`. Each vertex of these quads has its z-coordinate encoded with an intra-instance index, which is used in the vertex shader to fetch the splat order.

**Gsplat Data**: The `GsplatRendererImpl` class creates several `GraphicsBuffer`s on the GPU to hold the splat data: `PositionBuffer`, `ScaleBuffer`, `RotationBuffer`, `ColorBuffer`, and `SHBuffer`. It also creates an `OrderBuffer` which will later store the sorted indices of the splats. For 4DGS assets, it also creates `VelocityBuffer`, `TimeBuffer`, and `DurationBuffer`. The `GsplatRenderer` class uploads the data from the `GsplatAsset` arrays to these corresponding `GraphicsBuffer`s.

### Rendering Pipeline

The following two passes are performed each frame for every active camera.

#### Sorting Pass

This pass sorts the splats by their depth to the camera. The sorting is performed entirely on the GPU using `Gsplat.compute`. This compute shader leverages a highly optimized radix sort implementation from `DeviceRadixSort.hlsl`.

*   **Integration**: The sorting is initiated by custom render pipeline hooks: `GsplatURPFeature` for URP, `GsplatHDRPPass` for HDRP, or `GsplatSorter.OnPreCullCamera` for BiRP. These hooks call `GsplatSorter.DispatchSort`.
*   **Sorting Steps**:
    1.  **`InitPayload`** (Optional): If the payload buffer (`b_sortPayload`) has not been initialized, fill it with sequential indices (0, 1, 2, ... `SplatCount`-1). 
    2.  **`CalcDistance`**: For each splat, this kernel calculates its view-space depth, and stores them in the `b_sort` buffer which will be used as the sorting key.
        - For 3D-only assets, the key is calculated from the static center `pos0`.
        - For 4DGS assets, the key is calculated from `pos(t) = pos0 + vel * (t - time0)` using the per-renderer `TimeNormalized` parameter.
        - For splats outside the time window (`t` not in `[time0, time0 + duration]`), the compute pass writes an extreme key to push them towards the end of the sorted sequence (rendering still performs the final visibility gating in the shader).
    3.  **`DeviceRadixSort`**: The `Upsweep`, `Scan`, and `Downsweep` kernels execute a device-wide radix sort. It sorts the depth values in the `b_sort` buffer. Crucially, it applies the same reordering operations to the `b_sortPayload` buffer.
*   **Result**: After the sort, the `b_sortPayload` buffer (which is the `OrderBuffer` from `GsplatRendererImpl`) contains the original splat indices, now sorted from back-to-front based on their depth to the camera.

#### Render Pass

With the splats sorted, they can now be drawn using `Gsplat.shader`.

*   **Draw Call**: The `GsplatRendererImpl.Render` method issues a single draw call via `Graphics.RenderMeshPrimitives`. It uses GPU instancing to render multiple instances of the procedurally generated quad mesh, and a material is selected based on the desired `SHBands`. All necessary buffers (`OrderBuffer`, `PositionBuffer`, etc.) and parameters (`_MATRIX_M`, `_SplatCount`, etc.) are passed to the shader via a `MaterialPropertyBlock`.
*   **Vertex Shader**: 
    1.  **Index Calculation**: It determines the final splat `order` to render by combining the `instanceID` with the intra-instance index stored in the vertex's z-component.
    2.  **Fetch Sorted ID**: It uses this `order` to look up the actual splat `id` from the `_OrderBuffer`. This `id` corresponds to the correct, depth-sorted splat.
    3.  **Fetch Splat Data**: Using this sorted `id`, it fetches the splat's position, rotation, scale, color, and SH data from their respective buffers.
        - For 4DGS assets, it also fetches velocity/time/duration and evaluates the dynamic center at the current `TimeNormalized`.
        - If `t` is outside the `[time0, time0 + duration]` window, the vertex shader hard-clips the splat by outputting `discardVec` (no fragment contribution).
    4.  **Covariance & Projection**: It calculates the 2D covariance matrix of the Gaussian in screen space. This determines the shape and size of the splat on the screen. It performs frustum and small-splat culling for efficiency.
    5.  **Color Calculation**: The base color is taken from the `_ColorBuffer`. If SHs are used, `EvalSH` is called to calculate the view-dependent color component, which is then added.
    6.  **Vertex Output**: It calculates the final clip-space position of the quad's vertex by offsetting it from the splat's projected center based on the 2D covariance. The final color and UV coordinates (representing the position within the Gaussian ellipse) are passed to the fragment shader.
*   **Fragment Shader**:
    1.  It calculates the squared distance from the pixel to the center of the Gaussian ellipse using the interpolated UVs.
    2.  If the pixel is outside the ellipse (`A > 1.0`), it is discarded.
    3.  The final alpha is calculated using an exponential falloff based on the distance, modulated by the splat's opacity. Pixels with very low alpha are discarded.
    4.  The final color is the vertex color multiplied by the calculated alpha. An optional `Gamma To Linear` conversion can be applied before output.

### 4DGS Bounds Expansion

For 4DGS assets, the static asset bounds (`GsplatAsset.Bounds`) are expanded conservatively to avoid camera culling artifacts when splats move outside the original bounds.

- Import time collects motion statistics: `MaxSpeed = max(|velocity|)` and `MaxDuration = max(duration)`.
- At render time, the local bounds are expanded by `motionPadding = MaxSpeed * MaxDuration` (object space) before converting to `worldBounds`.

### `.sog4d` Keyframe Sequences

This package also supports SOG-style keyframe sequence bundles via the `.sog4d` extension.

**Importer (Editor-only):**

- The `.sog4d` file is treated as a ZIP bundle with a required `meta.json` at the root.
- `meta.json` is parsed via `JsonUtility` (unknown fields are ignored for forward compatibility).
- Per-frame WebP "data textures" are decoded and packed into `Texture2DArray` sub-assets:
  - `position_hi.webp` / `position_lo.webp` (u16 quantization per frame range)
  - `scale_indices.webp` (u16 indices into a scale codebook)
  - `rotation.webp` (quantized quaternion bytes)
  - `sh0.webp` (RGB = sh0Codebook indices, A = opacity)
  - `shN_labels.webp` (u16 labels into a global SH palette, either full per-frame or expanded from delta-v1)
- The importer generates a playable prefab (main object) with `GsplatSequenceRenderer` attached.

**Runtime playback:**

- `GsplatSequenceRenderer` evaluates the keyframe time mapping on CPU to produce `(i0, i1, a)` from `TimeNormalized`.
  - `uniform`: `t_i = i / (frameCount - 1)` (or `t_0 = 0` when `frameCount == 1`)
  - `explicit`: binary search over `frameTimesNormalized[]` (with a defined `a=0` branch for duplicate times)
- A dedicated compute shader decodes two frames and interpolates into float structured buffers:
  - `PositionBuffer`, `ScaleBuffer`, `RotationBuffer`, `ColorBuffer`, `SHBuffer`
- Sorting and rendering reuse the existing Gsplat pipeline on these float buffers, ensuring sort/render consistency.

**Notes on SH and compression:**

- Opacity is stored in `sh0.webp` alpha (SOG v2 style).
- Higher-order SH uses a palette + labels representation:
  - `shN_centroids.bin` (palette, f16/f32, little-endian)
  - labels are either `full` (per-frame WebP) or `delta-v1` (base labels + binary delta updates, expanded at import time).
