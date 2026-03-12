# WORKLOG: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## [2026-03-12 10:42:00 +0800] 任务名称: `.splat4d -> GsplatRenderer` 高斯边缘不透明感对照排查

### 任务内容
- 审计本仓库 `GsplatRenderer` / `Gsplat.shader` 的默认 Gaussian 呈现路径
- 对照 `playcanvas/supersplat` 的 splat shader
- 用最小数值验证确认用户主观观感是否可被公式差异解释

### 完成过程
- 先确认默认路径:
  - `GsplatRenderer.RenderStyle` 默认是 `Gaussian`
  - `_RenderStyleBlend` 由 `GsplatRendererImpl.SetRenderStyleUniforms` 下发到 shader
- 再对照 shader:
  - 当前仓库 Gaussian 核使用 `exp(-A * 4.0)`
  - `supersplat` 使用归一化后的 `normExp(A) = (exp(-4A) - exp(-4)) / (1 - exp(-4))`
- 最后做最小数值实验:
  - 比较 `A=0.90/0.95/0.99/1.00` 的 alpha
  - 统计边缘环 `A in [0.9,1.0]` 的平均 alpha 差异

### 总结感悟
- 当前问题更像“核函数边界归一化缺失”而不是 blend/sort 主问题
- `supersplat` 的关键点不是有没有截断,而是把截断边界重新归一到 0,这样边缘不会留下可见 pedestal

## [2026-03-12 11:41:00 +0800] 任务名称: `.splat4d -> GsplatRenderer` 高斯边缘 pedestal 修复

### 任务内容
- 将 `Gsplat.shader` 的 Gaussian alpha 从未归一化截断核改为归一化截断核
- 新增 EditMode 回归测试,锁定 `A=1 -> alpha=0` 的契约
- 在主工程 Unity MCP session 中完成编译与定向测试验证

### 完成过程
- 先通过 `supersplat` 对照与数值验证确认根因在核函数边界归一化缺失
- 再修改 shader:
  - 新增 `EvalNormalizedGaussianAlpha`
  - 替换旧的 `exp(-A_gauss * 4.0)` 路径
- 新增 `Tests/Editor/GsplatShaderKernelTests.cs`
  - 一条测试锁源码契约
  - 一条测试锁数学边界契约
- 最终使用主工程 Unity MCP session 跑定向 EditMode 测试
  - `passed=2/2`

### 总结感悟
- 这类“看起来像 blend/sort 的观感问题”,经常其实只是核函数边界条件不同
- 把视觉修复变成“源码契约 + 数学契约”两层测试,比只留主观截图更稳

## [2026-03-12 12:26:34 +0800] 任务名称: 补齐 `ImageConversion` 的 built-in package 依赖声明

### 任务内容
- 处理上一轮最小临时工程暴露出来的 package 自描述缺口
- 让 `wu.yize.gsplat` 在作为独立本地包接入时,不再隐式依赖宿主工程已有的 `ImageConversion` 模块

### 完成过程
- 先核对代码使用点:
  - `Runtime/GsplatSog4DRuntimeBundle.cs`
  - `Editor/GsplatSog4DImporter.cs`
  - `Tests/Editor/GsplatSog4DImporterTests.cs`
- 再核对 `package.json`
  - 确认之前只声明了 `com.unity.modules.physics`
- 最后在 `package.json` 中新增:
  - `com.unity.modules.imageconversion: 1.0.0`
- 并用 `python3` 做了最小 JSON 解析与依赖断言验证

### 总结感悟
- 这类问题的关键不在“主工程能不能编过”,而在“包本身是否对外自描述完整”.
- 主工程越完整,越容易把 UPM 包真正缺的内置模块依赖掩盖掉.

## [2026-03-12 12:38:06 +0800] 任务名称: 修复归一化高斯核在 Metal 下的全局常量初始化回归

### 任务内容
- 处理用户反馈的 `Gsplat/Standard` 在 Metal 下 shader 编译失败
- 保留上一轮“边界归一化”修复的数学语义,只修正常量初始化写法

### 完成过程
- 定位到 `Runtime/Shaders/Gsplat.shader` 中新增的两项全局常量:
  - `kGaussianExp4 = exp(-4.0)`
  - `kGaussianInvExp4 = 1.0 / (1.0 - kGaussianExp4)`
- 判断这是 Metal/HLSLcc 对“literal expression”要求更严格导致的回归
- 改为预计算字面量:
  - `0.0183156389`
  - `1.0186573604`
- 同步更新 `Tests/Editor/GsplatShaderKernelTests.cs` 的文本契约
- 最后用 Unity MCP 做刷新编译与定向测试验证

### 总结感悟
- shader 数学修复落地后,还要额外检查“常量是怎么写进去的”,不能默认所有后端都接受同样的全局初始化表达式.
- 这次最稳的做法不是回退算法,而是把平台不兼容的求值阶段前移到源码字面量.

## [2026-03-12 13:08:14 +0800] 任务名称: 撤回被真实场景证伪的 `normExp` 直移修复

### 任务内容
- 响应用户反馈的严重回归:
  - `ParticleDots` 可见
  - `Gaussian` 不可见
- 先恢复旧场景可见性,再继续研究更稳的边缘优化方案

### 完成过程
- 根据用户的真实场景反馈,确认当前回归是 Gaussian 专属输出路径的问题
- 将 `Runtime/Shaders/Gsplat.shader` 的 Gaussian alpha 回退到历史公式
- 删除上一轮基于“直接移植 `normExp` 可行”这一假设建立的测试文件
- 用户确认回退后显示恢复

### 总结感悟
- 这次最重要的收获不是“旧核更好”,而是:
  - 不能只对比 fragment alpha 公式就贸然移植 supersplat 的核函数
  - 真实场景可见性要求我们同时考虑 footprint、`A` 的定义域和裁剪语义

## [2026-03-12 15:06:00 +0800] 任务名称: 高斯边缘截断问题的保守 edge fade 实验

### 任务内容
- 继续研究 `.splat4d -> GsplatRenderer` 的 Gaussian 边缘截断感
- 不再直接移植 `supersplat` 的 `normExp`
- 改为落一个“只改外缘,不动中心”的保守 shader 实验

### 完成过程
- 先补读了当前 shader 的完整链路:
  - `temporalWeight -> baseAlpha -> ClipCorner -> A_gauss -> alphaGauss`
- 再对照 `supersplat` 主 shader:
  - 确认它的主路径里使用 `normExp(A)`
  - 同时主 shader 中未见 `clipCorner(...)` 调用
- 然后做最小数值实验:
  - 仅在 `A >= 0.90` 的最外圈额外乘 `1 - smoothstep(0.90, 1.0, A)`
  - 完整 kernel 积分只比旧核少约 `0.42%`
  - 对多数 `clip < 0.9` 的 splat 影响近似为 0
- 最后落地到 `Runtime/Shaders/Gsplat.shader`
  - 新增 `EvalConservativeGaussianEdgeFade`
  - Gaussian alpha 改成 `exp(-A*4) * edgeFade * i.color.a`
- 使用 Unity MCP 做了 A/B 实验:
  - 临时把 helper 改成 `return 1.0` 生成旧核对照图
  - 再切回保守 fade 版本生成新图
  - 两次切换都重新触发了 Unity 编译

### 动态验证
- Console 未出现新的 shader compile error
- 当前只看到一条无关 warning:
  - `MCP-FOR-UNITY: [WebSocket] Unexpected receive error: WebSocket is not initialised`
- 组件读取确认:
  - 两个目标对象的 `GsplatRenderer.RenderStyle` 都是 `0(Gaussian)`
- 截图证据:
  - 旧核: `Assets/Screenshots/gsplat_edge_old_kernel.png`
  - 新核: `Assets/Screenshots/gsplat_edge_conservative_fade.png`
- 当前能确认的结论:
  - 没有再出现“Gaussian 完全不显示”的严重回归
  - 在当前主相机距离下,视觉变化较 subtle,但至少没有整体可见性退化

### 总结感悟
- 这次真正重要的不是“把 supersplat 公式搬过来”,而是先承认当前仓库存在 `ClipCorner` 耦合链
- 在 footprint 未完全对齐前,最稳的做法是只碰外缘 10% 左右的 alpha,不要动主核中心区

## [2026-03-12 14:30:20 +0800] 任务名称: clone supersplat/engine 后重做 Gaussian 边缘问题对照与 `normExp` 复验

### 任务内容
- 重新对照 `playcanvas/supersplat` 与 `playcanvas/engine` 的 gsplat 显示链路
- 不再只凭 fragment 片段做判断,而是把 `clipCorner`、`minPixelSize`、`.splat4d` 时间链路一起核对
- 在当前 `AllCameras + refresh` 条件下,重新验证完整 `normExp` 是否仍会真实回退

### 完成过程
- 先补读 clone 下来的 engine chunk:
  - `gsplatCommon.js`
  - `gsplatCorner.js`
  - `gsplat.js`
  - `frag/gsplat.js`
  - `gsplat-instance.js`
- 再对照当前实现:
  - `Runtime/Shaders/Gsplat.hlsl`
  - `Runtime/Shaders/Gsplat.shader`
  - `Runtime/GsplatRendererImpl.cs`
- 静态对照结论:
  - `ClipCorner` 数学上与 engine 同构
  - `<2px` early-out 在默认阈值 2px 下也等价
  - 真正大的结构差异在 `.splat4d` 的 `temporalWeight -> baseAlpha -> ClipCorner` 链路与 Gaussian/ParticleDots morph
- 动态复验:
  - 临时把 shader 切回完整 `normExp`
  - Unity refresh 后无新的 shader error/warning
  - 在 `s1` 同一定点视角下重拍得到 `s1_close_normexp_allcams_rerun_1200.png`
  - 与 old/conservative 图比较,出现极大 coverage 回退
- 为了确认“是不是 temporalWeight 导致”,额外创建临时 Editor 探针脚本统计 `effectiveAlpha`
  - `s1` 结果显示绝大多数 visible splat 已经达到 `clip=1`
  - 这直接推翻了“`normExp` 主要是被 temporalWeight 压坏”的解释
- 最后把临时探针脚本删除,shader 回滚到保守 edge fade 版本

### 总结感悟
- 这轮最重要的收获是: 不能再把 supersplat 的差异理解成“只少一个 `normExp`”
- `s1` 的证据已经说明,即便高 alpha / window 模式下,直接替换 fragment 主核也会触发真实 coverage 崩塌
- 如果后续继续修,更应该从 footprint / coverage 标定去找差异,而不是再次把 fragment 主核整条替换

## [2026-03-12 15:28:30 +0800] 任务名称: 修复 `.ply` 资产无法直接拖入场景

### 任务内容
- 修复 `.ply` importer 的编辑器入口缺口
- 让 `.ply` 与 `.splat4d` 一样,导入后主对象就是可实例化的 `GameObject`
- 增加 EditMode 回归测试,锁定该行为

### 完成过程
- 先对照了 `Editor/GsplatImporter.cs` 与 `Editor/GsplatSplat4DImporter.cs`
- 再用 Unity MCP 动态确认 `Assets/Gsplat/ply/s1-point_cloud.ply` 的导入结果
  - 修复前它的 `assetType` 是 `Gsplat.GsplatAsset`
- 在 `Editor/GsplatImporter.cs` 中补了:
  - `gsplatAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath)`
  - `GameObject + GsplatRenderer`
  - `ctx.AddObjectToAsset("prefab", prefab)`
  - `ctx.SetMainObject(prefab)`
- 同时把 `.ply` importer 版本从 `1` bump 到 `2`
  - 这样旧 `.ply` 资产会自动重导入,不需要用户手动删缓存排查
- 新增 `Tests/Editor/GsplatPlyImporterTests.cs`
  - 验证 `.ply` 导入后的 main object 是 prefab
  - 验证 prefab 上自动挂载 `GsplatRenderer`
  - 验证 `renderer.GsplatAsset` 自动绑定且 sub-asset 仍保留
- 最后 refresh + compile + 运行单测

### 总结感悟
- 这次问题的关键不是解析失败,而是“导入入口形态错了”
- Unity 的 `ScriptedImporter` 一旦改变输出资产形态,要同步考虑 version bump
- 否则代码层已经修好,现场旧资产仍会保留老结果,非常像“修复没生效”

## [2026-03-12 17:28:10 +0800] 任务名称: 按用户结论恢复 `normExp`

### 任务内容
- 用户已确认当前整体偏暗主因是 tonemapping
- 因此不再保守回退 alpha 核
- 直接把 shader 恢复到 `normExp` 版本

### 完成过程
- 在 `Runtime/Shaders/Gsplat.shader` 中恢复 `EvalNormalizedGaussian`
- 将 Gaussian 片元 alpha 恢复为 `EvalNormalizedGaussian(A_gauss) * i.color.a`
- 保留 Metal 兼容的字面量常数写法
- refresh + compile 验证恢复成功

### 总结感悟
- 这次亮度问题最终不该继续和 gsplat alpha 核绑在一起讨论
- 先把 tonemapping 与 shader 核分离,再决定渲染风格,会更清晰
