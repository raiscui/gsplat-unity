## [2026-03-18 21:49:17 +0800] [Session ID: 505DEC94-AB29-42D5-B880-88BC2E74BFE0] 问题: `show-hide-switch-雷达` 后半段雷达粒子闪断

### 现象
- 用户人工验收发现:
  - 点击 `show-hide-switch-雷达` 后,
  - 雷达粒子已经 show 出来后,后半段会突然整批消失一次,
  - 随后又重新出现。

### 原因
- reverse dual-track 在 overlap 后半段存在一个尾段空窗:
  - `m_pendingGaussianToRadarFinalizeRadarMode` 仍为 true,
  - 但 `m_gaussianToRadarLidarShowOverlayActive` 已经结束。
- 旧逻辑的 `BuildLidarShowHideOverlayForThisFrame(...)` 在这种状态下会回退到共享 `m_visibilityState`。
- 共享状态此时可能还是 `Hiding` 或刚到 `Hidden`,从而把 LiDAR 误读成 hide 或 gate=0。

### 修复
- 在 `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 中增加 reverse finalize 尾段优先分支。
- 规则改为:
  - 只要 `m_pendingGaussianToRadarFinalizeRadarMode` 还没结束,
  - 即便专用 show overlay 已播完,
  - LiDAR 也保持稳定可见,
  - 不再回退到共享 hide/hidden。

### 验证
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过,0 warning / 0 error。
- Unity EditMode 通过:
  - `BuildLidarShowHideOverlay_GaussianToRadarFinalizeTail_DoesNotFallBackToHide`
  - `PlayGaussianToRadarScanShowHideSwitch_DoesNotFlashRadarBetweenOverlapAndStableMode`
  - `PlayGaussianToRadarScanShowHideSwitch_DelaysRadarShowUntilSharedTrigger_AndRestoresStableRadarMode`
  - `DualTrackSwitchTriggerProgress01_AffectsBothDirections`
  - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`
