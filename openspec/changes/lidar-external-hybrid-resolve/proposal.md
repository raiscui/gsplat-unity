## Why

方案1 `lidar-external-capture-supersampling` 已经把 frustum external GPU capture 的质量路径收敛到“提高 capture fidelity,但继续保持 point texel read + nearest-surface 语义”。这条路线能明显减轻 depth 台阶,但当 external target 的 silhouette 很细、斜面很多,或者 capture texel 与 LiDAR ray 的相位关系不理想时,仍然会保留边缘阶梯和断续感。

现在需要继续推进方案2,但前提不能变: 不要为了让边缘更顺就把前后表面错误混在一起。因此这次 change 不走普通 blur 或 naive bilinear depth mixing,而是引入两条都能独立开关、也能组合使用的 resolve 路线:

- edge-aware nearest resolve
- subpixel jitter resolve

## What Changes

- 为 frustum external GPU capture 引入新的 hybrid resolve 质量能力:
  - `2x2 / 3x3 edge-aware nearest resolve`
  - `Quad4 subpixel jitter resolve`
- 为上述两条 resolve 路线分别提供独立的公开开关,并允许以下组合:
  - 两者都关闭: 保持当前方案1行为
  - 只开 edge-aware
  - 只开 subpixel
  - 两者都开
- 明确两者同时开启时的固定执行顺序:
  - 先生成 subpixel candidate uv
  - 再对每个 candidate 做 edge-aware neighborhood resolve
  - 最后在所有候选结果中选择最近且可信的 winner
- 为 edge-aware resolve 增加可配置但受约束的 neighborhood 策略:
  - `Off`
  - `Kernel2x2`
  - `Kernel3x3`
  - 需要基于深度差 / ray-distance 一致性做边缘保护,避免跨边界混样
- 为 subpixel resolve 增加固定 pattern 的候选采样模式:
  - `Off`
  - `Quad4`
  - 不使用随机 jitter,避免 temporal stability 退化
- 明确 hybrid resolve 的回退语义:
  - edge-aware 过滤失败时,回退到中心 point sample
  - color 必须跟随最终 depth winner,不能独立平均
- 更新 Inspector、README、CHANGELOG 与自动化测试,把这两条 resolve 路线的用途、组合方式、性能成本与语义边界写清楚。

## Capabilities

### New Capabilities
- `gsplat-lidar-external-hybrid-resolve`: 定义 frustum external GPU capture 如何通过 edge-aware nearest resolve 与 subpixel jitter resolve 提升 external hit 的边缘稳定性,以及它们的独立开关、组合顺序、回退语义和验证要求。

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
  - 新增 edge-aware resolve mode 配置
  - 新增 subpixel resolve mode 配置
  - 新增两者组合时的明确质量档位与行为约束
