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

## [2026-03-24 12:00:00 +0800] [Session ID: unknown] 任务名称: 落地方案2 `lidar-external-hybrid-resolve`

### 任务内容
- 按 OpenSpec `tasks.md` 实施 frustum external hybrid resolve。
- 把 `edge-aware nearest resolve + Quad4 subpixel resolve` 真的接到 runtime / editor / compute / docs / tests。
- 尽量把验证推进到 Unity EditMode 真实执行阶段。

### 完成过程
- 在 `Runtime/GsplatUtils.cs` 中新增:
  - `GsplatLidarExternalEdgeAwareResolveMode`
  - `GsplatLidarExternalSubpixelResolveMode`
  - 以及对应 sanitize helper
- 在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中新增两个公开字段,补默认值、校验和 GPU capture 调用参数。
- 在 `Editor/GsplatRendererEditor.cs` / `Editor/GsplatSequenceRendererEditor.cs` 中新增 Hybrid Resolve 区块,把组合顺序、成本差异和“不是 blur”写进 HelpBox。
- 在 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` 中补齐:
  - 两个 compute property ID
  - `TryCaptureExternalHits` 新参数
  - mode 变化触发重新 resolve 的缓存失效逻辑
  - Debug helper,供 EditMode tests 锁定 Quad4 / kernel 布局
- 在 `Runtime/Shaders/Gsplat.compute` 中把原来的单 texel external resolve 重构成:
  - center point sample
  - `Quad4` subpixel candidate 生成
  - `Kernel2x2 / Kernel3x3` edge-aware neighborhood 过滤
  - final nearest winner 选择
  - color follows winner
- 在 `Tests/Editor/GsplatLidarScanTests.cs` / `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs` 中补了默认值、sanitize、deterministic Quad4、kernel 布局、参数下发和 source contract 测试。
- 在 `README.md` / `CHANGELOG.md` 中同步文档口径。

### 总结感悟
- 这次最关键的工程点,其实不是 shader 公式本身,而是把“mode 切换也必须触发重新 resolve”这条状态语义补上,否则 Inspector 调档位时很容易看到旧缓存假象。
- `Off + Off` 保持原路径不动,把新能力都收进 helper,这种改法虽然比直接硬改长一点,但回归风险明显更低。

## [2026-03-24 12:34:31 +0800] [Session ID: 20260324_8] 任务名称: 收口 `lidar-external-hybrid-resolve` 的最终验证

### 任务内容
- 完成 OpenSpec `4.4`。
- 把 Unity MCP 测试占用、会话抖动和真正有效的验证证据区分清楚。
- 修掉一条因 helper 重构带来的测试契约漂移。

### 完成过程
- 先重新读取支线 `task_plan`、`notes`、`WORKLOG` 与 `openspec/changes/lidar-external-hybrid-resolve/tasks.md`,确认唯一未完成项就是 `4.4`。
- 再通过 `mcpforunity://editor/state`、`~/.unity-mcp/unity-mcp-status-717de14b.json` 与 `lsof` 交叉确认:
  - Unity Editor 仍在运行
  - MCP bridge listener 存在
  - 旧 job `16b3...` 实际是初始化超时后自动失败,不是真的一直在跑
- 然后重新绑定 active instance,恢复动作通道,并继续执行 Unity EditMode 验证。
- 在整程序集首轮验证里定位到唯一相关红项:
  - `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests.ExternalGpuResolve_UsesLinearDepthTextureBeforeRayDistanceConversion`
- 最后把这条测试从“锁旧的 static/dynamic 内联源码”改成“锁统一 helper 下的 linear depth -> ray depth point load 语义”,并重新完成编译与整程序集复验。

### 总结感悟
- 这轮最重要的收获,不是“又跑了一次测试”,而是把无效证据排除了:
  - `tests_running` 不一定代表 job 真在执行
  - `summary.total = 0` 的 `test_names` 结果不能当通过
- Unity MCP 在 domain reload 后可能换端口,所以验证时要同时看:
  - `editor/state`
  - `~/.unity-mcp/unity-mcp-status-*.json`
  - `lsof` 的实际 listener

## [2026-03-24 13:09:06 +0800] [Session ID: 20260324_8] 任务名称: 修复 Metal 下 `Gsplat.compute` 的 struct ternary 编译错误

### 任务内容
- 处理用户反馈的 Metal shader compile error。
- 确认是不是 `ExternalResolveSample` 被当成 `?:` 结果触发平台限制。
- 在不改语义的前提下做最小修复并完成现场编译验证。

### 完成过程
- 先读取 `Gsplat.compute` 报错行附近源码,确认 `ResolveExternalCandidate(...)` 在返回处用了:
  - `return bestNeighborhoodSample.Valid != 0 ? bestNeighborhoodSample : centerSample;`
- 再全文件搜索 `?:`,确认只有这一处是“自定义 struct 作为三元结果”,其余都是 numeric/向量写法。
- 然后把该返回改成显式 `if (bestNeighborhoodSample.Valid != 0) return bestNeighborhoodSample; return centerSample;`
- 同步更新 `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`,把源码契约从“锁三元表达式”改成“锁显式分支返回”。
- 最后完成两层验证:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`
  - Unity reload 后读取 Console 中 `Gsplat.compute` / `conditional operator` 相关错误

### 总结感悟
- 这类问题的重点不是“Metal 真麻烦”,而是要先把类型层级看清:
  - numeric ternary 在 HLSL 里没问题
  - struct ternary 才是触发点
- 一旦根因坐实,最稳的修法通常是语言级等价改写,而不是加平台分支

## [2026-03-24 14:12:25 +0800] [Session ID: 20260324_8] 任务名称: 修复 LiDAR compute 单轴 dispatch 超出 65535 group limit

### 任务内容
- 处理用户现场报错:
  - `Thread group count is above the maximum allowed limit`
- 解释这个上限是否可以“突破”
- 把 scan 与 external capture 两条 compute 提交链路一起改成可分批 dispatch

### 完成过程
- 先定位 `GsplatLidarScan.TryRebuildRangeImage(...)` 的三次单轴 `DispatchCompute(x,1,1)`。
- 再确认 `GsplatLidarExternalGpuCapture` 里也有同类单轴 dispatch 风险。
- 然后在 `GsplatUtils` 里抽出线性 compute dispatch chunk 规划 helper:
  - 给出每个 chunk 的 `dispatchBaseIndex`
  - 给出每个 chunk 的 `groupsX`
- 在 `GsplatLidarScan` / `GsplatLidarExternalGpuCapture` 中改成循环分批 dispatch。
- 在 `Gsplat.compute` 中新增 `_LidarDispatchBaseIndex`,让相关 kernel 都能从偏移后的全局线性索引开始处理。
- 最后补 `GsplatUtilsTests` 锁定边界:
  - 1024 item -> 单 chunk
  - `16776960 + 1` item -> 自动拆成两次 dispatch

### 总结感悟
- 这次最关键的认知点是:
  - 不能把“线程组上限”理解成“最多只能处理这么多数据”
  - 真正受限的是“单次 dispatch 的单个维度”
- 对线性 kernel 来说,只要把 `dispatchBaseIndex` 设计好,分批 dispatch 就是最自然也最稳的解法

## [2026-03-24 14:29:56 +0800] [Session ID: 20260324_9] 任务名称: 放开 `LidarBeamCount` 的历史 `512` runtime clamp

### 任务内容
- 处理用户提出的 `LidarBeamCount` 被限制到 `512` 的问题
- 判断这个限制是不是底层硬上限
- 如果只是历史防呆值,就把它真正放开并补回归测试

### 完成过程
- 先回读 `GsplatRenderer` / `GsplatSequenceRenderer` 的 `ValidateLidarSerializedFields()` 与字段声明,确认 `512` 只存在于 runtime clamp,字段本身没有面板硬上限
- 再回读 `TryGetEffectiveLidarLayout`、`EnsureRangeImageBuffers`、`EnsureLutBuffers` 与 external hit 相关链路,确认底层都按一般 `beamCount * azimuthBins` 工作,没有隐藏 `512` 常量
- 结合上一轮已经完成的“LiDAR compute 分批 dispatch”修复,确认旧 clamp 不再承担“规避 `65535` group limit”的职责
- 然后在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中去掉 `LidarBeamCount > 512` 钳制,并把 tooltip / 注释改成“无硬上限,但成本线性上升”
- 最后在 `Tests/Editor/GsplatLidarScanTests.cs` 中增加两条回归测试,锁定 `2048` beamCount 不会再被 validate 压回 `512`

### 总结感悟
- 这次最重要的不是“删掉一个 if”,而是先确认它背后还有没有别的隐含假设
- 对这种历史防呆值,最稳的做法是:
  - 先排掉结构性上限
  - 再改公开说明
  - 最后补测试把新契约锁住
