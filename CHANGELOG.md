# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Added `GsplatActiveCameraOverride` component to explicitly pick a Game/VR camera as the active camera in `ActiveCameraOnly` mode (supports priority + last-enabled tie-break).
- Added optional Editor diagnostics (`GsplatSettings.EnableEditorDiagnostics`) to collect camera/sort/draw traces and auto-dump context when Metal skips draw calls due to missing buffer bindings.
- Added optional burn-ring visibility animation (Show/Hide) for `GsplatRenderer` and `GsplatSequenceRenderer` (easeInOutQuad ring expansion, smoke-like noise, configurable center, separate show/hide ring+trail widths, per-splat grow/shrink + position warp distortion, adjustable `WarpStrength`, and Inspector buttons). Disabled by default to keep legacy behavior.
- Added `VisibilityNoiseMode` dropdown for the burn-ring visibility animation to switch between `ValueSmoke` (default), `CurlSmoke` (curl-like warp field), and `HashLegacy` (legacy comparison).
- Added `ShowGlowStartBoost` for the burn-ring visibility animation, and tuned show/hide glow so the burn front ring stays on the outer side and brighter (boost), with an inward-decaying afterglow tail (brighter interior, less abrupt outer rim).
- Added `ShowGlowSparkleStrength` to modulate the show ring glow with curl-noise sparkle/twinkle ("embers" flicker).
- Added burn-ring visibility animation particle-size tuning: `ShowSplatMinScale`, `ShowRingSplatMinScale`, `ShowTrailSplatMinScale`, and `HideSplatMinScale` (separates "spatial ring width" from "splat size", and reduces the "tiny dots" look on the burn front).
- Tuned hide splat size shrink to follow a faster-then-slower (easeOutCirc-like) curve, keeping a non-zero minimum size to avoid "disappearing too fast" during the burn tail.
- Added `GsplatRenderStyle` (`Gaussian` / `ParticleDots`) and `SetRenderStyle(...)` API for `GsplatRenderer` and `GsplatSequenceRenderer` to switch between standard Gaussian splats and screen-space particle dots (solid discs with a soft edge), with a default animated morph transition (`easeInOutQuart`, `1.5s`) and adjustable `ParticleDotRadiusPixels` (radius in screen pixels).
- Added an experimental LiDAR scan visualization mode for `GsplatRenderer` and `GsplatSequenceRenderer` (regular `128 x 2048` point grid, first return occlusion via GPU range image, `UpdateHz=10` full rebuild with `RotationHz=5` scan head + 1-revolution afterglow, `Depth` / `SplatColorSH0` color modes, `LidarDepthOpacity` for `Depth` visibility, and `HideSplatsWhenLidarEnabled` to disable splat sort/draw while keeping buffers for LiDAR sampling). Disabled by default.
- Added `LidarShowHideWarpPixels` to tune RadarScan(LiDAR) show/hide jitter amplitude in screen pixels, decoupled from point size.
- Added LiDAR-specific show/hide glow tuning (`LidarShowHideGlowColor`, `LidarShowGlowIntensity`, `LidarHideGlowIntensity`) so RadarScan glow can be adjusted independently from Gaussian.
- Added LiDAR-specific show/hide noise tuning (`LidarShowHideNoiseScale`, `LidarShowHideNoiseSpeed`) so RadarScan show/hide noise can be adjusted independently (defaults to reusing global `NoiseScale` / `NoiseSpeed` when the LiDAR overrides are negative).
- Added `SetRenderStyleAndRadarScan(...)` API to `GsplatRenderer` and `GsplatSequenceRenderer` for a single-call switch between Gaussian/ParticleDots and RadarScan mode (enables LiDAR + forces ParticleDots when RadarScan is active).

### Changed

- Removed the max clamp for `LidarAzimuthBins` (only keeps a minimum guard), allowing higher azimuth resolution when needed.
- Tuned burn-ring hide afterglow to linger longer behind the glow front (eased fade timing + two-stage shrink, avoids the "glow passes and everything vanishes" look).
- Tuned burn-ring hide warp so it cannot push splats outward past the burn front (keeps the afterglow trail visually inside the ring even with strong `WarpStrength`).
- LiDAR scan visualization now uses a single `LidarBeamCount` (no Up/Down split) and samples vertical beam directions uniformly over `[LidarDownFovDeg..LidarUpFovDeg]` (more down-beams naturally come from a larger downward FOV range).
- LiDAR point cloud rendering now uses screen-space square points with alpha blending (opaque when alpha=1), and the `Depth` color mode maps depth from cyan -> blue -> purple -> red.
- The Inspector `Render Style` quick-action row now includes `RadarScan(动画)`, and `Gaussian(动画)` / `ParticleDots(动画)` now also disable RadarScan in the same action so switching is bidirectional.
- Removed the max clamp for `LidarShowHideWarpPixels` (was 64), allowing larger values for stronger RadarScan show/hide jitter.

### Fixed

- Fixed Metal draw-skip warnings ("requires a ComputeBuffer ... but none provided") by binding all required `GraphicsBuffer` resources on a per-renderer material instance (and rebinding before each draw call, treating the 4D buffers as always-required bindings, using dummy buffers when needed).
- Fixed Editor Edit Mode flicker in `ActiveCameraOnly` mode by making SceneView the deterministic default active camera (GameView selection uses a sticky "last interacted viewport" hint, and you can always force a Game/VR camera via `GsplatActiveCameraOverride`).
- Fixed Editor Edit Mode flicker in SRP where a camera can trigger multiple `beginCameraRendering` invocations within the same frame, but draw submission was happening only once in `ExecuteAlways.Update` (draw submission is now aligned to the camera callbacks in Edit Mode `ActiveCameraOnly`).
- Fixed splats disappearing in the GameView (Edit Mode) while dragging `TimeNormalized` in the Inspector (GameView stays "sticky" as the active viewport even when Inspector takes focus).
- Fixed very slow keyframe `.splat4d(window)` playback by sorting and rendering only the active time segment (sub-range sort/draw) when the asset matches the non-overlapping segment pattern.
- Fixed burn-ring visibility animation (Show/Hide) in Editor Edit Mode appearing to "not play" unless the viewport repaints (e.g., mouse movement), by requesting Editor repaints while the animation is in progress.
- Fixed the burn-ring hide animation sometimes leaving a few splats lingering near the end due to outward edge jitter, by making fade/shrink use a more stable edge distance (ring/glow still jitters).
- Fixed RenderStyle switching (Gaussian <-> ParticleDots) popping for splats near the screen edge (those culled by the dot frustum cull at the transition endpoints): they now smoothly fade in/out instead of abruptly disappearing/appearing at the start/end of the animation.
- Fixed LiDAR point cloud looking "too far" / like a thick outer shell by storing the projected depth onto the bin-center ray (instead of the Euclidean distance), making sampling and reconstruction consistent.
- Fixed LiDAR mode switches popping: `Depth <-> SplatColorSH0` now blends smoothly, and `RadarScan -> Gaussian/ParticleDots` now fades out LiDAR visibility instead of hard-cutting.
- Fixed LiDAR `Depth(动画)` / `SplatColor(动画)` Inspector buttons appearing to do nothing by preventing the LiDAR color transition sync from restarting the animation every frame.
- Fixed a black-frame gap when switching `Gaussian/ParticleDots -> RadarScan` with `HideSplatsWhenLidarEnabled=true`, by delaying splat hiding until radar fade-in is nearly complete.
- Fixed abrupt bright-sphere popping at `Hide` start by making hide ring/trail radius start very small and then ramp up (geometry-first, not transparency-first).
- Fixed `Show/Hide` interrupt glitches by introducing source-mask compositing: pressing `Hide` during `Show` (or `Show` during `Hide`) now keeps the current visible distribution as a source mask and overlays the new transition on top, avoiding reverse playback and full-frame pops during rapid toggles.
- Fixed `Show/Hide` not affecting `RadarScan`: LiDAR point-cloud rendering now consumes the same show/hide overlay semantics (`mode/progress/sourceMask`) as `ParticleDots`, so radar mode also gets center-out reveal/burn behavior during visibility transitions.
- Fixed missing particle-noise feel in `RadarScan` show/hide transitions: LiDAR now forwards `VisibilityNoiseMode/NoiseStrength/NoiseScale/NoiseSpeed` into the show/hide mask path (primary mask, source-mask compositing, and ring glow edge jitter), so radar reveal/burn no longer looks unnaturally "clean" compared to `ParticleDots`.
- Fixed missing "ParticleDots-like" noise motion in `RadarScan` show/hide by adding edge-weighted screen-space point jitter (noise-driven position warp) during transition, so radar points now exhibit visible granular displacement instead of only brightness-mask noise.
- Fixed `CurlSmoke` parity in `RadarScan` show/hide: LiDAR `CurlSmoke` now uses a curl-like vector field, and the screen-space jitter amplitude is scaled by `WarpStrength` (0 disables, higher values increase motion).
- Fixed `RadarScan` show/hide glow: LiDAR now draws the ring in the alpha mask (so show glow is visible), adds an inward afterglow tail (so hide glow lingers longer), and uses a colored additive glow overlay controlled by LiDAR-specific glow parameters.

## [1.1.4] - 2026-02-23

### Added

- Added `GsplatSettings.CameraMode` (`ActiveCameraOnly` / `AllCameras`) and Project Settings UI to control whether Gsplat sorts/renders for all cameras or only the active camera.

### Changed

- Default camera mode is now `ActiveCameraOnly` (performance-first). In Play Mode / Player, only the resolved active Game/VR camera triggers GPU sorting and rendering; switch to `AllCameras` if you need multiple cameras (portals / probes / helper cameras) to see the splats.

### Fixed

- Fixed `ActiveCameraOnly` failing to resolve a Game/VR camera in `-batchmode -nographics` on some Editor environments (ensures EditMode tests can run without ActiveCamera resolution returning null).
- Fixed SceneView "visible/invisible" flicker in the Editor while using `ActiveCameraOnly` by keeping SceneView rendering stable in Edit Mode (and allowing SceneView cameras to drive sorting even when internal camera instances are noisy).

## [1.1.3] - 2026-02-23

### Added

- Supports streaming data from RAM to VRAM ([#6](https://github.com/wuyize25/gsplat-unity/issues/6)). An option `Async Upload` is added to `GsplatRenderer` to enable this feature.
- `.splat4d v2` supports SH delta-v1 at runtime: when `labelsEncoding=delta-v1`, `GsplatRenderer` applies per-frame label updates and uses a compute shader to scatter-write updated SH coefficients into `SHBuffer` (falls back to static frame0 SH when compute is unavailable).
- Added `GsplatSettings.AllowSceneViewSortingWhenFocusedInPlayMode` to balance Play Mode performance and SceneView correctness.

### Fixed

- `.sog4d` importer now has a macOS Editor WebP decode fallback (embedded `libwebp`) when `ImageConversion.LoadImage` does not support WebP.
- Avoids sorter init errors in `-batchmode -nographics` (no graphics device) so EditMode tests can run without unrelated compute shader kernel log errors.
- SceneView cameras in SRP (URP/HDRP) now trigger GPU sorting via `RenderPipelineManager.beginCameraRendering`, so sorting stays in sync even without configuring HDRP CustomPassVolume or URP RendererFeature.
- In the Editor, changing `TimeNormalized` forces a SceneView repaint to avoid the "switch to GameView to refresh" workflow.

## [1.1.2] - 2025-11-20

### Fixed

- Fixed the issue where rendering did not work properly on Mac with Unity 6 ([#9](https://github.com/wuyize25/gsplat-unity/issues/9)).

## [1.1.1] - 2025-11-19

### Fixed

- Fixed an error when importing the PLY file generated from Postshot ([#8](https://github.com/wuyize25/gsplat-unity/issues/8)). The `GsplatImporter` now supports PLY files with arbitrary property order, and the PLY file may not contain the unused normal property.

## [1.1.0] - 2025-10-13

### Added

- Supports BiRP, URP and HDRP in Unity 2021 and later versions.

### Fixed

- Fixed a NullReferenceException when opening the project using URP or HDRP.

## [1.0.3] - 2025-09-15

### Fixed

- More space was allocated to the SH buffer than is actually required.

## [1.0.2] - 2025-09-10

### Added

- Added an `SHDegree` option to `GsplatRenderer`, which sets the order of SH coefficients used for rendering.

### Fixed

- Fixed an error in SH calculation.

## [1.0.1] - 2025-09-09

### Changed

- Split out `GsplatRendererImpl` from `GsplatRenderer`.

## [1.0.0] - 2025-09-07

### Added

- This is the first release of Gsplat, as a Package.


[unreleased]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.4...HEAD
[1.1.4]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.3...v1.1.4
[1.1.3]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.2...v1.1.3
[1.1.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/wuyize25/gsplat-unity/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/wuyize25/gsplat-unity/releases/tag/v1.0.0
