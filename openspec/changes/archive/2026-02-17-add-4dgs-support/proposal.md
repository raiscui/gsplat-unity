## Why

当前仓库只支持静态 3D Gaussian Splatting(3DGS).
在动态场景里,我们需要在同一份高斯数据上表达时间变化.
典型数据来自 FreeTimeGS/4DGS 思路,会为每个高斯提供位置、速度、时间、持续时间等 4D 属性.

把 4DGS 能力引入当前渲染管线,可以在 Unity(HDRP/URP/BiRP) 中直接播放动态 3DGS.
同时,通过 VFX Graph 后端,可以获得更友好的时间驱动与特效工作流.

## What Changes

- 支持导入 canonical 4D PLY:
  - 在现有 3DGS PLY 字段基础上,新增 `velocity(vx,vy,vz)`,`time`,`duration`.
  - 当 4D 字段缺失时,保持旧行为(静态渲染).
- 支持导入 `.splat4d` 二进制格式(基于 `SplatVFX` 的 `.splat` 思路扩展):
  - 每条记录包含 position/scale/rotation/color,并额外包含 `velocity/time/duration`.
  - 用于更快的导入与更顺滑的 VFX Graph 工作流.
- 扩展 GPU 数据布局:
  - 新增 velocity/time/duration 对应的 `GraphicsBuffer`.
- 扩展 GPU 排序与渲染:
  - 排序 pass 在时间 `t` 下按 `pos(t)` 计算深度 key.
  - 渲染 shader 在时间 `t` 下按 `pos(t)` 投影椭圆高斯.
  - 在时间窗外(`t` 不在 `[t0, t0+duration]`)的高斯直接不可见.
- 增加播放控制接口:
  - 提供 `TimeNormalized`(0..1) 的脚本/Timeline 可驱动参数.
  - 支持 speed/loop 等基础播放控制.
- 增加 VFX Graph 后端(可选):
  - 按 `SplatVFX` 风格提供更"一键可用"的工作流:
    - 通过 binder 把 GPU buffers 绑定到 `VisualEffect`.
    - 允许基于 `.splat4d` 的导入产物直接驱动 VFX Graph.
  - 目标是尽量接近现有 Gsplat 的视觉结果,但会有明确的规模上限与回退策略.
- 增加资源预算与告警:
  - 估算显存占用,在高风险规模(例如 >10M + SH3)时给出明确告警.
  - 提供可配置的自动降级策略(例如降低 SH 阶数或限制最大 splat 数).

## Capabilities

### New Capabilities
- `4dgs-core`: 4D 高斯数据导入、播放时运动模型、时间窗可见性、GPU 排序与渲染联动.
- `4dgs-playback-api`: 可脚本/Timeline 驱动的 `TimeNormalized` 播放控制与参数约束.
- `4dgs-vfx-backend`: 基于 Unity VFX Graph 的可选渲染后端与 GPU buffer 绑定器,用于驱动与对照.
- `4dgs-resource-budgeting`: 显存/带宽预算估算、告警与自动降级策略,避免 OOM 与不可解释失败.

### Modified Capabilities
- (无)当前仓库尚未建立 OpenSpec specs,本次为新增能力.

## Impact

- 影响的代码区域:
  - `Editor/GsplatImporter.cs`(PLY header 与数据读取扩展)
  - `Runtime/GsplatAsset.cs`(新增 4D 数组字段)
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatRendererImpl.cs`(新增 GPU buffers 与上传路径)
  - `Runtime/GsplatSorter.cs`/`Runtime/Shaders/Gsplat.compute`(排序 key 基于 `pos(t)`)
  - `Runtime/Shaders/Gsplat.shader`/`Runtime/Shaders/Gsplat.hlsl`(渲染基于 `pos(t)` 与时间窗裁剪)
  - HDRP/URP/BiRP 注入点保持不变,但需要验证行为一致性.
- 可能新增依赖:
  - VFX Graph 后端会使用 `UnityEngine.VFX`(需要项目安装 Visual Effect Graph 包).
  - 该依赖应保持可选,不应导致未安装 VFX Graph 的项目编译失败.
