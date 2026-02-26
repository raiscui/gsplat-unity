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

### Changed

- Tuned burn-ring hide afterglow to linger longer behind the glow front (eased fade timing + two-stage shrink, avoids the "glow passes and everything vanishes" look).
- Tuned burn-ring hide warp so it cannot push splats outward past the burn front (keeps the afterglow trail visually inside the ring even with strong `WarpStrength`).

### Fixed

- Fixed Metal draw-skip warnings ("requires a ComputeBuffer ... but none provided") by binding all required `GraphicsBuffer` resources on a per-renderer material instance (and rebinding before each draw call, treating the 4D buffers as always-required bindings, using dummy buffers when needed).
- Fixed Editor Edit Mode flicker in `ActiveCameraOnly` mode by making SceneView the deterministic default active camera (GameView selection uses a sticky "last interacted viewport" hint, and you can always force a Game/VR camera via `GsplatActiveCameraOverride`).
- Fixed Editor Edit Mode flicker in SRP where a camera can trigger multiple `beginCameraRendering` invocations within the same frame, but draw submission was happening only once in `ExecuteAlways.Update` (draw submission is now aligned to the camera callbacks in Edit Mode `ActiveCameraOnly`).
- Fixed splats disappearing in the GameView (Edit Mode) while dragging `TimeNormalized` in the Inspector (GameView stays "sticky" as the active viewport even when Inspector takes focus).
- Fixed very slow keyframe `.splat4d(window)` playback by sorting and rendering only the active time segment (sub-range sort/draw) when the asset matches the non-overlapping segment pattern.
- Fixed burn-ring visibility animation (Show/Hide) in Editor Edit Mode appearing to "not play" unless the viewport repaints (e.g., mouse movement), by requesting Editor repaints while the animation is in progress.
- Fixed the burn-ring hide animation sometimes leaving a few splats lingering near the end due to outward edge jitter, by making fade/shrink use a more stable edge distance (ring/glow still jitters).

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
