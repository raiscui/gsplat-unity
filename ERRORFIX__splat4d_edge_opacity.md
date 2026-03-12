# ERRORFIX: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## 2026-03-12 11:40:00 +0800 问题: `.splat4d -> GsplatRenderer` 的高斯边缘有明显边界感

### 现象
- 用户观察:
  - `.splat4d` 经 `GsplatRenderer` 呈现时,高斯基元边缘有轮廓感
  - 观感不像 `supersplat` 那样自然收尾
- 静态证据:
  - 当前 `Runtime/Shaders/Gsplat.shader` 使用 `alphaGauss = exp(-A_gauss * 4.0) * i.color.a`
  - 同时只截断到 `A <= 1`,并没有把截断边界重新归一到 `0`
- 动态数值证据:
  - `A=1.0` 时当前核 alpha 仍为 `0.018316`
  - 同口径下 `supersplat` 归一化核在 `A=1.0` 时 alpha 为 `0`
  - `A in [0.9, 1.0]` 的平均边缘 alpha,当前实现约为 `supersplat` 的 `5.258x`

### 原因
- 当前实现使用了“未归一化的截断高斯核”
- 结果是边界 `A=1` 仍保留可见 alpha pedestal
- 对高 opacity splat,这部分 alpha 足以穿过 `1/255` discard 阈值,形成可见外沿

### 修复
- 修改 `Runtime/Shaders/Gsplat.shader`
- 新增归一化 helper:
  - `kGaussianExp4`
  - `kGaussianInvExp4`
  - `EvalNormalizedGaussianAlpha(float a)`
- 将 Gaussian alpha 从:
  - `exp(-A_gauss * 4.0) * i.color.a`
  - 改为
  - `EvalNormalizedGaussianAlpha(A_gauss) * i.color.a`
- 结果:
  - `A=0` 仍保持 `1`
  - `A=1` 收敛为 `0`

### 回归测试
- 新增:
  - `Tests/Editor/GsplatShaderKernelTests.cs`
- 锁定两条契约:
  - shader 源码必须包含归一化核 helper 且不能回退到旧公式
  - 数学语义必须满足 `A=0 -> 1`, `A=1 -> 0`

### 验证
- 主工程 Unity MCP 编译:
  - 成功进入测试执行阶段,未见本次修改引入的新增编译错误
- 主工程定向 EditMode 测试:
  - `job_id = 9dfd800b79404af3a49c7574676dbc77`
  - `passed=2, failed=0`
- 通过的测试:
  - `NormalizedGaussianKernel_BoundaryConvergesToZero`
  - `StandardShader_UsesNormalizedGaussianKernel`

### 结论
- 这次“边缘边界感”的主要根因已经定位并修复
- 修复已在主工程真实 Unity session 中通过定向测试验证

## 2026-03-12 12:26:34 +0800 问题: package 清单未声明 `ImageConversion` 对应的 built-in module

### 现象
- 最小 Unity 工程只引用本地 `wu.yize.gsplat` 时,会报:
  - `Runtime/GsplatSog4DRuntimeBundle.cs(...): ImageConversion does not exist in the current context`
- 主工程不暴露这个问题,因为宿主 manifest 已经带了对应模块

### 原因
- package 代码直接使用 `ImageConversion`
- 但 `package.json` 没有声明 `com.unity.modules.imageconversion`
- 结果是 package 自描述依赖不完整

### 修复
- 修改 `package.json`
- 新增依赖:
  - `com.unity.modules.imageconversion: 1.0.0`

### 验证
- `python3` 解析 `package.json` 成功
- 依赖键值断言通过

### 结论
- 这次修复补齐的是 UPM 清单层缺口
- 目的不是改变主工程行为
- 而是让 package 在外部工程中更完整地自描述其模块依赖

## 2026-03-12 12:38:06 +0800 问题: 归一化高斯核常量在 Metal 下不是合法的全局字面量初始化

### 现象
- macOS / Metal 编译 `Gsplat/Standard` 时报错:
  - `kGaussianInvExp4: initial value must be a literal expression`
- 报错位置:
  - `Runtime/Shaders/Gsplat.shader(905)`

### 原因
- 上一轮修复把高斯归一化常量写成了全局表达式初始化:
  - `const float kGaussianExp4 = exp(-4.0);`
  - `const float kGaussianInvExp4 = 1.0 / (1.0 - kGaussianExp4);`
- 在 Metal/HLSLcc 编译链上,这类初始化不被接受为“literal expression”

### 修复
- 将上述两项改成预计算字面量:
  - `0.0183156389`
  - `1.0186573604`
- 保留函数:
  - `EvalNormalizedGaussianAlpha(float a)`
- 同步更新 `Tests/Editor/GsplatShaderKernelTests.cs` 的源码文本断言

### 验证
- Unity MCP refresh 后,Console 未再出现该 shader error
- 定向 EditMode 测试:
  - `Gsplat.Tests.GsplatShaderKernelTests`
  - `2/2` passed

### 结论
- 这是 shader 常量初始化写法的跨平台兼容问题
- 不是高斯核数学修复本身有误

## 2026-03-12 13:08:14 +0800 问题: 直接移植 `supersplat` 的 `normExp` 后,真实旧场景里的 Gaussian 完全不显示

### 现象
- 用户在真实场景中确认:
  - `ParticleDots` 有显示
  - `Gaussian` 完全不显示
- 将 shader 回退到旧核后,显示恢复

### 原因判断
- 这条问题不能再简单归因为“边界 pedestal 太亮”
- 当前更准确的判断是:
  - `supersplat` 的 `normExp` 不能孤立移植到我们现在这条 Gaussian 路径
  - 至少还有 footprint / `A` 定义 / 裁剪语义中的某个条件没有一起对齐

### 修复
- 先回退 `Runtime/Shaders/Gsplat.shader` 中的 Gaussian alpha 到历史公式:
  - `exp(-A_gauss * 4.0) * i.color.a`
- 同时删除上一轮基于错误假设建立的 `GsplatShaderKernelTests`

### 验证
- 用户动态验证:
  - 回退后显示恢复

### 结论
- 这次回退不是放弃边缘优化
- 而是先撤销已被真实场景证伪的错误修法
- 下一步应改成“更保守的边缘处理方案”研究

## 2026-03-12 14:26:00 +0800 问题: 追加 Markdown 笔记时,未使用单引号 heredoc 导致 shell 把反引号内容当成命令执行

### 现象
- 在向 `notes__splat4d_edge_opacity.md` 追加包含大量反引号的 Markdown 段落时
- zsh 输出了多条:
  - `command not found`
  - `bad pattern`
  - `unknown file attribute`
- 这不是仓库代码报错
- 是 shell 在展开 Markdown 中的反引号/特殊字符

### 原因
- 追加命令错误地放在双引号包裹的 shell 字符串里
- 虽然正文里用了 heredoc,但外层没有严格用单引号 heredoc 把整段文字隔离
- 结果违反了本项目的规则:
  - Markdown 正文包含反引号时,必须使用 `cat <<'EOF'`

### 修复
- 先检查目标笔记文件尾部,确认是否被污染或截断
- 再使用单引号 heredoc 重新追加同一段内容
- 后续这类六文件追加一律使用 `cat <<'EOF'`

### 验证
- 待重新追加后补充

### 补充验证
- 已检查 `notes__splat4d_edge_opacity.md` 尾部
- 确认上一次损坏段落未继续扩散
- 已通过单引号 heredoc 追加一条“更正记录”作为有效覆盖版本

## 2026-03-12 15:06:00 +0800 问题: `.splat4d` Gaussian 边缘有截断感,但直接移植 `normExp` 又会破坏真实场景可见性

### 现象
- 用户最初观察到 Gaussian 边缘有边界感
- 之前直接移植 `supersplat` 的 `normExp(A)` 后,真实场景里曾出现 Gaussian 完全不显示
- 这次继续排查时,静态阅读发现当前仓库还有一条 `temporalWeight -> baseAlpha -> ClipCorner -> A_gauss` 耦合链

### 原因判断
- 当前不能再把问题简化成“旧核少一个归一化”
- 更准确的判断是:
  - 当前仓库与 supersplat 至少同时存在
    - 片元核差异
    - 顶点几何裁剪口径差异
- 因此直接整体替换 Gaussian 核风险过高

### 修复
- 在 `Runtime/Shaders/Gsplat.shader` 中新增保守 helper:
  - `EvalConservativeGaussianEdgeFade(float a)`
- 只在最外圈 `A >= 0.90` 额外乘:
  - `1 - smoothstep(0.90, 1.0, A)`
- 保留旧核中心区:
  - `A < 0.90` 时完全不变

### 验证
- 数值验证:
  - 完整 kernel 面积平均积分只比旧核少约 `0.42%`
  - 对 probe 中大多数 `clip < 0.9` 的 splat 影响近似为 0
- Unity MCP 动态验证:
  - 通过 helper 临时切回旧核,生成 old/new A/B 截图
  - Console 未出现新的 shader 编译错误
  - 当前未再复现“Gaussian 完全不显示”

### 结论
- 这次修改更像“安全的边缘去 pedestal 实验”
- 不是对 supersplat 的完全算法对齐
- 当前最可信的结论是:
  - 它避免了上一轮的严重可见性回归
  - 同时给边界留出了一条更温和的收尾路径

## [2026-03-12 15:28:30 +0800] 问题: `.ply` 资产无法直接拖入场景

### 问题现象
- `.ply` 文件能被导入
- 但在 Unity Project 面板里无法像 `.splat4d` 一样直接拖到 Scene / Hierarchy

### 原因
- `Editor/GsplatImporter.cs` 只把结果导入成 `GsplatAsset` 子资源
- 原先没有创建 `GameObject + GsplatRenderer`
- 原先没有 `ctx.SetMainObject(...)`
- 此外,仅修改 importer 代码还不够
  - 如果不 bump `[ScriptedImporter(version, ...)]` 的 version
  - 已存在的 `.ply` 资产不会自动重导入,仍会保留旧的 `GsplatAsset` 主对象形态

### 修复
- 在 `Editor/GsplatImporter.cs` 中:
  - 给 `gsplatAsset` 补文件名
  - 创建 main prefab
  - 自动挂 `GsplatRenderer`
  - 绑定 `renderer.GsplatAsset`
  - `ctx.AddObjectToAsset("prefab", prefab)`
  - `ctx.SetMainObject(prefab)`
- 将 `.ply` importer version 从 `1` 提升到 `2`

### 验证
- Unity MCP `manage_asset search`
  - 修复前: `assetType = Gsplat.GsplatAsset`
  - 修复后: `assetType = UnityEngine.GameObject`
- Unity Console:
  - 新 error/warning = 0
- EditMode 测试:
  - `Gsplat.Tests.GsplatPlyImporterTests.Import_FixturePly_CreatesPlayablePrefabMainObject`
  - 结果: 通过

### 以后避免再犯
- 只要 `ScriptedImporter` 的输出资产形态发生变化(main object 类型、sub-asset 结构、默认绑定关系),就要第一时间检查是否需要 version bump
- 对“能导入但不能拖放/实例化”的问题,优先查 main object 类型,不要先怀疑解析算法
