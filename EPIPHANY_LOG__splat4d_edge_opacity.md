# EPIPHANY_LOG: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## [2026-03-12 10:42:00 +0800] 主题: 高斯 splat 的“边缘边界感”可以只由核函数边界 pedestal 产生,不一定是 blend 或排序问题

### 发现来源
- 对照本仓库 `Runtime/Shaders/Gsplat.shader` 与 `playcanvas/supersplat` 的 splat shader
- 再做最小数值验证

### 核心问题
- 当前仓库把 Gaussian 核截断在 `A <= 1` 后,仍直接使用 `exp(-4A)`
- 这会让 `A=1` 的边界仍保留 `exp(-4) ≈ 0.0183` 的 alpha pedestal
- `supersplat` 则把同一截断高斯重新归一到边界为 0

### 为什么重要
- 这类视觉问题很容易被误判成:
  - 排序错误
  - 预乘 alpha 错误
  - blend state 错误
- 但实际上仅仅是核函数边界条件不同,就足以造成明显观感差异

### 当前结论
- 当前最强解释是“高 opacity splat 在边界仍有可见 alpha pedestal”
- 这和用户看到的轮廓感高度一致
- temporal gaussian 只会放大或缩小整体 alpha,不是这次最主要的结构性差异

## [2026-03-12 11:43:00 +0800] 主题: 视觉核函数修复完成后,最小临时工程能顺便暴露 package 自描述依赖缺口

### 发现来源
- 为了验证本次 shader 修复,额外搭建了只包含本地 package 的最小 Unity 工程

### 核心问题
- 最小工程并没有暴露本次 shader 问题
- 反而先暴露出 package 对 `ImageConversion` 的隐式模块依赖

### 为什么重要
- 这说明主工程环境会掩盖 package 的真实可移植性问题
- 对外发布 UPM 包时,这种隐式依赖会在用户项目中变成“明明引用了包却编译不过”的硬错误

### 当前结论
- 本次高斯边缘修复已经完成且主工程验证通过
- 但 package 级别仍存在一条值得后续专项修复的依赖完整性问题

### 后续讨论入口
- 优先回看 `Runtime/GsplatSog4DRuntimeBundle.cs:1448`
- 再核对 `package.json` 中是否需要补 `com.unity.modules.imageconversion`

## [2026-03-12 15:06:00 +0800] 主题: supersplat 与当前仓库的关键差异不只在 fragment `normExp`, 还在 vertex 侧是否做 `ClipCorner`

### 发现来源
- 对照当前 `Runtime/Shaders/Gsplat.shader` / `Runtime/Shaders/Gsplat.hlsl`
- 对照 `supersplat` 的 `src/shaders/splat-shader.ts`
- 再结合真实场景里“直接移植 `normExp` 会让 Gaussian 消失”的动态证据

### 核心问题
- 当前仓库的 Gaussian 输出受一条耦合链控制:
  - `temporalWeight -> baseAlpha -> ClipCorner -> A_gauss -> alphaGauss`
- `supersplat` 主 shader 虽然用了 `normExp(A)`,但主路径里未见 `clipCorner(...)`

### 为什么重要
- 这意味着 future work 不能再把“只改 fragment alpha”当成天然安全的修复方向
- 任何想继续对齐 supersplat 的尝试,都必须把 footprint 与几何裁剪一起纳入验证

### 当前结论
- 更稳的当前策略是:
  - 保留旧核主体
  - 只在外缘做保守 fade
- 如果未来还要进一步逼近 supersplat,应该先搭一个能稳定观察单个 splat 轮廓的近景验证场景

### 后续讨论入口
- `Runtime/Shaders/Gsplat.shader`
- `Runtime/Shaders/Gsplat.hlsl:153`
- `Assets/Screenshots/gsplat_edge_old_kernel.png`
- `Assets/Screenshots/gsplat_edge_conservative_fade.png`

## [2026-03-12 14:31:10 +0800] 主题: `s1` 的高 alpha / window 模式证据表明,当前与 supersplat 的主差异已经不能再归因给 temporal alpha

### 发现来源
- clone 下来的 `playcanvas/engine` shader chunk 对照
- 在当前 `AllCameras + refresh` 条件下重新跑完整 `normExp`
- 临时 effective alpha 探针统计 `s1` / `ckpt` 当前帧分布

### 核心问题
- `s1` 是 `window` 模式
- 当前帧里 `97.7%` 的 visible splat 已满足 `Amax>=0.9`
- `93.8%` 的 visible splat 已满足 `clip=1`
- 但完整 `normExp` 仍会让同一定点视角出现巨大 coverage 回退

### 为什么重要
- 这说明“当前保守 fade 太弱”与“完整 `normExp` 会崩”之间,不是简单的一维强弱问题
- 真正差异更可能在:
  - footprint / coverage 校准
  - 数据稀疏度与表面填充依赖的尾部能量
  - 或当前 renderer 的几何/混合语义与 supersplat 的组合差异

### 当前结论
- `temporalWeight` 只能解释一部分资产(例如 `ckpt`)为什么更不敏感
- 但它已经不能解释 `s1` 的 `normExp` 崩塌
- 因此未来若继续对齐 supersplat,要把 fragment 主核替换看成高风险动作

### 后续讨论入口
- `Runtime/Shaders/Gsplat.shader`
- `Runtime/Shaders/Gsplat.hlsl`
- `Assets/Screenshots/s1_close_old_kernel_allcams_waited.png`
- `Assets/Screenshots/s1_close_normexp_allcams_rerun_1200.png`

## [2026-03-12 15:28:30 +0800] 主题: Unity `ScriptedImporter` 改变输出资产形态时,不做 version bump 会制造“修好了但现场没生效”的假象

### 发现来源
- 修复 `.ply` importer 无法拖入场景时
- 静态上已经补了 `prefab + SetMainObject`
- 但第一次 refresh 后,现场 `s1-point_cloud.ply` 仍显示为 `Gsplat.GsplatAsset`

### 核心问题
- 仅修改 importer 代码,不代表已存在资产会自动重导入成新形态
- 当 main object 类型发生变化时,旧导入缓存会让现场继续保留旧结果

### 为什么重要
- 这会严重干扰调试口径
- 很容易误判成:
  - 代码没生效
  - Unity 没刷新
  - 或者修复方向本身不对

### 当前结论
- 这次 `.ply` 入口修复真正完整的落地点包括两部分:
  - 修 importer 输出逻辑
  - bump `[ScriptedImporter]` version
- 以后遇到“导入行为改了,但旧资产表现不变”,优先检查 importer version

### 后续讨论入口
- `Editor/GsplatImporter.cs`
- `Tests/Editor/GsplatPlyImporterTests.cs`

## [2026-03-12 16:18:00 +0800] 主题: 当前与 supersplat 的关键差异已经从“主核公式”收敛到“尾部是否被提前裁掉”

### 发现来源
- 逐文件对照 `Runtime/Shaders/Gsplat.shader`
- 对照 `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts`
- 再补一个最小数值验证

### 核心问题
- 当前仓库虽然已经恢复 `normExp`
- 但 forward path 仍保留 `alpha < 1/255` discard
- 同时 vertex 还保留 `ClipCorner`
- supersplat 自定义 shader 则:
  - forward path 不做这条 alpha discard
  - vertex 也没有调用 `clipCorner`

### 为什么重要
- 这说明当前“边缘还是更有边界感”已经不能再简单归因给 `normExp` 主核是否一致
- 真正差异已经上移到:
  - 尾部 alpha 是否允许继续积累
  - 低 alpha splat 的 footprint 是否被几何缩小

### 当前结论
- 当前仓库和 supersplat 的主要分叉,已经不是 fragment 主核公式
- 而是 coverage 保留策略
- 这也解释了为什么“只恢复 `normExp`”之后,观感仍不会完全等同 supersplat

### 后续讨论入口
- `Runtime/Shaders/Gsplat.shader:861`
- `Runtime/Shaders/Gsplat.shader:956`
- `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:188`
