## Why

当前 frustum external GPU capture 会先把 external mesh 渲染到离屏 depth/color RT,再在 `Gsplat.compute` 里按整数 texel 读取线性 depth. 这条链路在 external target 的轮廓、斜面和远处小结构上容易出现明显的像素阶梯,用户看到的结果会表现成台阶感、断续细缝和不够稳定的表面边缘。

这次 change 先采用风险最低的方案1: external capture supersampling. 目标不是立刻引入更复杂的 edge-aware depth resolve,而是先用更高分辨率的 capture RT 减小 texel 台阶,同时保持当前“最近表面 + point resolve”的几何语义不变。

## What Changes

- 把 external capture supersampling 明确为 frustum external GPU capture 的正式质量方案:
  - 用更高分辨率的离屏 depth/color capture 降低 external depth 的像素阶梯。
  - 不改变当前 nearest-surface / nearest-hit 的基础语义。
- 明确并收敛 `LidarExternalCaptureResolutionMode` / `LidarExternalCaptureResolutionScale` / `LidarExternalCaptureResolution` 的质量定位:
  - `Scale` 模式作为台阶问题的首选缓解手段。
  - supersample 的宽高推导、硬件上限 clamp 与运行时诊断要保持一致且可预测。
- 为 external capture 增加更明确的用户提示与验证:
  - Inspector 文案要明确说明 supersampling 是降低 depth 台阶的首选方案。
  - README / CHANGELOG / OpenSpec 要同步记录该质量路径。
- 补齐自动化测试,锁定 supersampling 的分辨率推导与运行时行为:
  - Auto / Scale / Explicit 三种模式的尺寸决策
  - scale 值的 sanitize / clamp
  - supersample 生效时 external capture 不应改变最近表面选择语义

## Capabilities

### New Capabilities
- `gsplat-lidar-external-capture-quality`: 定义 frustum external GPU capture 如何通过 supersampling 提升 external depth/color capture 质量,以及分辨率模式、scale 推导、用户提示和验证语义。

### Modified Capabilities
- (无)

## Impact

- Affected runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - `Runtime/Shaders/Gsplat.compute`
- Affected editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- Affected tests/docs:
  - `Tests/Editor/*`
  - `README.md`
  - `CHANGELOG.md`
- Public surface:
  - external capture 现有分辨率模式与 scale 参数会被正式定义为“降低台阶”的质量控制入口
  - Inspector / 文档会新增更明确的 supersampling 指引
