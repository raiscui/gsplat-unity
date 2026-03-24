## [2026-03-23 17:24:59] [Session ID: 20260323_8] 任务名称: RadarScan subpixel 粒子消失修复

### 任务内容
- 修复 RadarScan / LiDAR 粒子在 `size < 1px` 时容易消失的问题。
- 保留已经可用的 stable jitter 方向,不再把问题误判成 `warp`。
- 同步更新 shader 契约测试,避免旧断言把错误行为重新锁回去。

### 完成过程
- 先通过 shader 静态阅读确认: 当前默认 `LegacySoftEdge` 下,`<1px` 点会真的生成 `<1px` billboard 几何。
- 再用最小像素中心采样脚本验证: subpixel footprint 确实可能完全打不到任何片元。
- 然后在 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中把“真实半径”和“coverage 支撑宽度”拆开。
- 最后更新 `Tests/Editor/GsplatLidarShaderPropertyTests.cs`,并完成 C# 编译与 Unity 包级测试验证。

### 总结感悟
- subpixel 点的正确修法,不是把真实半径重新钳大,而是分离“视觉半径”和“raster / coverage 支撑半径”。
- 这类问题如果只盯 fragment alpha,很容易漏掉更早的光栅阶段退化。

## [2026-03-23 21:14:02 +0800] [Session ID: 20260323_9] 任务名称: OpenSpec 快进方案1 external capture supersampling

### 任务内容
- 继续用户指定的 `$openspec-ff-change` 流程。
- 为方案1 `lidar-external-capture-supersampling` 补齐 `design`、`specs`、`tasks` 三个 artifact。
- 把 change 推进到可直接进入实现的 apply-ready 状态。

### 完成过程
- 先读取 `openspec instructions design/specs/tasks` 的官方约束。
- 再读取 `lidar-camera-frustum-external-gpu-scan` 与 `lidar-external-targets` 两个相近 change 的 design / tasks / spec 写法。
- 同时回看 `GsplatLidarExternalGpuCapture.cs`、`Gsplat.compute` 与现有 EditMode tests,确认 `Auto / Scale / Explicit` 现在的真实语义与 point depth resolve 证据。
- 然后完成以下文件:
  - `openspec/changes/lidar-external-capture-supersampling/design.md`
  - `openspec/changes/lidar-external-capture-supersampling/specs/gsplat-lidar-external-capture-quality/spec.md`
  - `openspec/changes/lidar-external-capture-supersampling/tasks.md`
- 最后执行 `openspec status --change "lidar-external-capture-supersampling"`,确认 `4/4 artifacts complete`。

### 总结感悟
- 这次方案1最重要的设计边界,不是“怎么把边缘弄顺”,而是“先不破坏 nearest-surface 语义”。
- 当前仓库其实已经有 `Scale` 这套 API 了,更好的做法不是继续堆新参数,而是把它正式定义为 supersampling 质量入口。

## [2026-03-23 22:07:39 +0800] [Session ID: 20260323_10] 任务名称: 落地 OpenSpec change `lidar-external-capture-supersampling`

### 任务内容
- 按 `openspec-apply-change` 实施方案1对应的 OpenSpec change。
- 补齐 external capture supersampling 的代码注释、Inspector 提示、README / CHANGELOG 文案和回归测试。
- 更新 OpenSpec `tasks.md`,让 change 从“可实施”推进到“已实施完成”。

### 完成过程
- 先读取 `openspec instructions apply` 与当前 `tasks.md`,确认本轮共有 `13` 个待完成任务。
- 然后核对 runtime / editor / tests / docs 现状,确认 capture-size 逻辑主体已经存在,主要缺口在“语义护栏”和“推荐用法说明”。
- 在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中强化了 resolution mode / scale / explicit 的 tooltip。
- 在 `Editor/GsplatRendererEditor.cs` / `Editor/GsplatSequenceRendererEditor.cs` 中强化了 external capture help box,明确 `Scale > 1` 是首选缓解手段,并补充成本与语义边界说明。
- 在 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` 与 `Runtime/Shaders/Gsplat.compute` 中补了中文注释,把 point texel read、Auto 基准、depth/color 同布局这些关键口径写死。
- 在 `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs` 中增加了:
  - invalid scale sanitize / downsample 测试
  - point texel read 不得退化成 `Sample` / `SampleLevel` 的测试
  - depth / surfaceColor / depthStencil 共用同尺寸的测试
- 最后更新 `README.md`、`CHANGELOG.md` 与 OpenSpec `tasks.md`,并完成编译与 targeted Unity EditMode 验证。

### 总结感悟
- 这次最值得保留的做法,不是“多写一个 supersample 功能”,而是把现有参数真正定义成可理解、可验证、可维护的质量入口。
- 对这种已有半成品实现的能力,最正确的推进方式通常不是重写,而是先补齐语义、文档和测试护栏。

## [2026-03-24 11:31:47 +0800] [Session ID: 20260324_1] 任务名称: 方案2 hybrid resolve proposal 起草

### 任务内容
- 继续方案2 explore。
- 把“edge-aware nearest resolve + subpixel jitter resolve, 且支持独立开关和组合启用”的方向整理成新的 OpenSpec proposal 草案。

### 完成过程
- 先确认方案1 `lidar-external-capture-supersampling` 已 complete,避免把方案2混进旧 change。
- 再基于当前 `ResolveExternalFrustumHits` 的 point texel read 现状,收敛方案2的新增能力边界。
- 新建 change:
  - `openspec/changes/lidar-external-hybrid-resolve/`
- 完成 proposal:
  - `openspec/changes/lidar-external-hybrid-resolve/proposal.md`
- proposal 里明确写了:
  - 两条 resolve 路线都做
  - 两者都能独立开关
  - 两者都开时的固定执行顺序
  - edge-aware 失败回退到中心样本
  - color 跟随最终 depth winner

### 总结感悟
- 方案2的关键不是“加更多采样”本身,而是把两个质量路径的职责拆清楚:
  - subpixel 负责增加候选
  - edge-aware 负责防串边
  - final winner selection 负责保 nearest-hit 语义

## [2026-03-24 11:53:09 +0800] [Session ID: unknown] 任务名称: 一次性补齐方案2 `lidar-external-hybrid-resolve` OpenSpec artifacts

### 任务内容
- 继续用户要求的“方案2 explore 收口后一次性出完”。
- 为 `lidar-external-hybrid-resolve` 补齐 `design`、`specs`、`tasks` 三个 artifact。
- 把 change 推进到 apply-ready,为下一步直接实施做准备。

### 完成过程
- 先重新读取支线 `task_plan/notes/WORKLOG`、已有 `proposal.md` 和 `openspec instructions design/specs/tasks`。
- 再把本轮已经收敛的设计口径写死:
  - `LidarExternalEdgeAwareResolveMode = Off / Kernel2x2 / Kernel3x3`
  - `LidarExternalSubpixelResolveMode = Off / Quad4`
  - 两者都开时顺序固定为“subpixel candidate -> edge-aware neighborhood resolve -> final nearest winner”
  - edge-aware 失败时回退中心 point sample
  - color 跟随最终 depth winner
- 然后创建以下文件:
  - `openspec/changes/lidar-external-hybrid-resolve/design.md`
  - `openspec/changes/lidar-external-hybrid-resolve/specs/gsplat-lidar-external-hybrid-resolve/spec.md`
  - `openspec/changes/lidar-external-hybrid-resolve/tasks.md`
- 最后执行 `openspec status --change "lidar-external-hybrid-resolve" --json`,确认 `proposal/design/specs/tasks` 全部为 `done`。

### 总结感悟
- 方案2最核心的边界,不是“把边缘弄顺”这么抽象,而是明确它是“保 nearest-surface 语义的候选选样 resolve”。
- 这次先把 API、顺序、回退和颜色绑定规则写清楚,比直接冲进实现更稳,后面落代码时不容易因为局部效果好看而破坏整体语义。
