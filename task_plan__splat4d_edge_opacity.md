# 任务计划: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## 目标

确认用户观察到的“高斯基元边缘有边界感,没有自然过渡”的现象,是否来自当前 `GsplatRenderer` 的渲染算法实现。
如果存在和 `playcanvas/supersplat` 的关键差异,定位到具体代码路径,给出可证据支撑的结论与修复方向。

## 阶段

- [ ] 阶段1: 读取历史上下文与建立调查分支
- [ ] 阶段2: 本仓库渲染路径静态审计
- [ ] 阶段3: `supersplat` 对照研究
- [ ] 阶段4: 差异归因与最小验证
- [ ] 阶段5: 结论,必要修复建议与交付

## 关键问题

1. 用户感知到的“边缘边界感”,是来自空间高斯核本身,还是来自 temporal gaussian / LOD / sorting / blend 其中之一?
2. 当前 shader 是否把 alpha 形状从标准高斯核改成了带硬边或混合插值的形态?
3. `supersplat` 的片元核、alpha 阈值、blend 策略与本仓库有哪些关键区别?
4. 如果确实不同,差异是有意风格化效果,还是会损伤标准高斯视觉质量的实现偏差?

## 候选方向

- 方向A: 最佳方案
  - 做到“同现象 -> 同代码路径 -> 同对照实现”三级证据闭环
  - 必要时补最小探针或回归测试,把结论固定下来
- 方向B: 先能用方案
  - 先完成静态代码对照与公式差异归纳
  - 如果证据已经足够强,先给结论和修复建议,后续再补动态验证

## 当前主假设与备选解释

- 主假设:
  - 当前 `Gsplat.shader` 片元阶段并不是纯 2D 高斯核,而是引入了 `gaussian <-> dot` 风格混合或额外阈值裁切,这会让边缘出现更明显的轮廓感
- 备选解释:
  - 问题不在核形状,而在 alpha 预乘/颜色空间/排序或 temporal opacity 映射
- 推翻主假设所需证据:
  - 若 `supersplat` 与当前 shader 的核公式本质一致,但实际差异主要来自排序、裁切阈值或时域权重,则当前主假设不成立

## 做出的决定

- 先不改代码。
- 先按“现象 -> 假设 -> 验证计划 -> 结论”执行调查。
- 优先复用已有支线上下文,但这次另开 `__splat4d_edge_opacity` 文件集,避免污染 OpenSpec 立项记录。

## 遇到的错误

- (待补)

## 状态

**目前在阶段1**
- 正在读取相关上下文、定位本地 shader 路径,并准备对照 `playcanvas/supersplat` 的实现。

## 2026-03-12 10:20:00 +0800 追加行动

- 当前行动目的:
  - 把这次“高斯边缘不透明感”调查单独外部化
  - 避免分析过程淹没在其他 `.splat4d` 任务里
- 下一步行动:
  - [ ] 回读本仓库与 `.splat4d` / `GsplatRenderer` 直接相关的 shader / runtime 代码
  - [ ] 获取 `playcanvas/supersplat` 对应渲染代码
  - [ ] 做公式级对照,再决定是否需要最小实验验证

## 2026-03-12 10:24:00 +0800 阶段进展

- 已完成本仓库第一轮静态审计:
  - 主 pass 为预乘 alpha `Blend One OneMinusSrcAlpha`
  - 默认 Gaussian 风格下,片元 alpha 公式是 `exp(-A * 4.0)`
  - 同时存在 `A > 1` 与 `alpha < 1/255` 两层裁切
- 下一步:
  - 对照 `playcanvas/supersplat` 的 splat shader
  - 判断它是否也采用同类截断高斯,还是使用了不同的 footprint / alpha 标定

## 2026-03-12 10:35:00 +0800 阶段进展

- 已补完最小数值验证:
  - 当前核在 `A=1` 时 alpha 仍为 `0.018316`
  - `supersplat` 在 `A=1` 时归一为 `0`
  - `A in [0.9,1.0]` 的平均边缘 alpha,当前实现约是 `supersplat` 的 `5.258x`
- 已确认默认运行时路径:
  - `GsplatRenderer.RenderStyle` 默认就是 `Gaussian`
  - shader 最终走的正是当前这条未归一化核公式
- 下一步:
  - 收敛结论口径
  - 判断是否还需要补一个更强的备选解释,例如颜色空间或 temporal gaussian 是否会主导该现象

## 2026-03-12 10:42:00 +0800 最终状态

- [x] 阶段1: 读取历史上下文与建立调查分支
- [x] 阶段2: 本仓库渲染路径静态审计
- [x] 阶段3: `supersplat` 对照研究
- [x] 阶段4: 差异归因与最小验证
- [x] 阶段5: 结论,必要修复建议与交付

**目前已完成阶段5**
- 已确认本次最关键差异是 Gaussian 核边界归一化:
  - 当前实现边界 `A=1` 时仍残留 alpha pedestal
  - `supersplat` 会把边界归一到 `0`
- 结论口径:
  - 这是算法层差异,而且已经有静态代码证据 + 动态数值证据共同支撑
- 当前未执行代码修改:
  - 等待用户确认是否直接按 `supersplat` 口径修正 shader

## 2026-03-12 10:48:00 +0800 用户要求继续后的执行计划

- 当前行动目的:
  - 将已确认的 Gaussian 核边界归一化差异正式落地为 shader 修复
  - 用最小回归测试锁住 `A=1 -> alpha=0` 的行为,避免后续回退
- 下一步行动:
  - [ ] 修改 `Runtime/Shaders/Gsplat.shader`,把 Gaussian alpha 改为归一化截断核
  - [ ] 新增一个最小 EditMode 回归测试,校验 shader 文本包含归一化核 helper
  - [ ] 运行本地验证:
    - Python 数值验证
    - 相关测试或最小静态检查
  - [ ] 回写 ERRORFIX / WORKLOG / notes

## 2026-03-12 11:34:00 +0800 用户已启动 Unity MCP session, 从未完成步骤继续

- 恢复点:
  - 上一轮未完成步骤是“Unity 侧真实编译和测试验证”
  - 之前临时工程验证拿到的是独立编译错误线索,但主工程因 session 不在线未能直接确认
- 当前行动目的:
  - 回到主工程的真实 Unity session
  - 刷新编译,读取 Console,运行 `GsplatShaderKernelTests`
- 下一步行动:
  - [ ] 重新探测当前 Unity MCP 会话是否就绪
  - [ ] 请求脚本编译并等待完成
  - [ ] 读取 Console 中与本次修复相关的 error/warning
  - [ ] 运行 `Gsplat.Tests.GsplatShaderKernelTests`

## 2026-03-12 11:44:00 +0800 最终验证状态

- [x] 修改 `Runtime/Shaders/Gsplat.shader`,把 Gaussian alpha 改为归一化截断核
- [x] 新增一个最小 EditMode 回归测试,校验 shader 文本包含归一化核 helper
- [x] 运行本地验证:
  - [x] Python 数值验证
  - [x] 主工程 Unity MCP 定向 EditMode 测试
- [x] 回写 ERRORFIX / WORKLOG / notes

**目前已完成本轮修复与验证**
- 主工程 Unity 定向测试结果:
  - `GsplatShaderKernelTests` passed `2/2`
- 当前可交付结论:
  - Gaussian 边缘 pedestal 修复已经落地并验证通过
- 额外发现已外部化:
  - package 存在 `ImageConversion` 模块依赖自描述不完整的问题,已记入 `LATER_PLANS` / `EPIPHANY_LOG`

## 2026-03-12 12:26:34 +0800 继续收尾: 补齐 `ImageConversion` package 依赖缺口

- 当前行动目的:
  - 处理上一轮在最小临时工程里暴露出来的 package 自描述依赖缺口
  - 避免 `wu.yize.gsplat` 作为独立本地包接入时,因为宿主工程没有显式带上 `ImageConversion` 模块而直接编译失败
- 当前已确认的事实:
  - 代码实际使用点存在于:
    - `Runtime/GsplatSog4DRuntimeBundle.cs`
    - `Editor/GsplatSog4DImporter.cs`
    - `Tests/Editor/GsplatSog4DImporterTests.cs`
  - 当前 `package.json` 只声明了:
    - `com.unity.modules.physics`
  - 官方 Unity built-in package 文档确认:
    - `ImageConversion` 对应 built-in package 名就是 `com.unity.modules.imageconversion`
- 下一步行动:
  - [ ] 修改 `package.json`,补 `com.unity.modules.imageconversion`
  - [ ] 做最小静态验证,确认清单结构正确且依赖口径一致
  - [ ] 回写 `notes / WORKLOG / ERRORFIX / LATER_PLANS`

## 2026-03-12 12:26:34 +0800 `ImageConversion` 依赖缺口修复完成

- 已完成的动作:
  - [x] 修改 `package.json`,补 `com.unity.modules.imageconversion`
  - [x] 做最小静态验证,确认 JSON 结构与依赖键值正确
  - [x] 回写 `notes / WORKLOG / ERRORFIX / LATER_PLANS`
- 关键结果:
  - `package.json:8` 已新增 `com.unity.modules.imageconversion: 1.0.0`
- 当前结论:
  - package 清单层的 `ImageConversion` 自描述依赖缺口已补齐
  - 当前没有证据显示还需要额外 asmdef 改动

## 2026-03-12 12:35:46 +0800 回归修复: Metal 不接受当前归一化高斯常量初始化表达式

- 当前行动目的:
  - 处理用户刚反馈的主工程真实编译错误
  - 让上一轮新增的归一化高斯核在 Metal 下也能通过 shader 编译
- 已观察到的现象:
  - Unity 报错:
    - `Shader error in Gsplat/Standard: kGaussianInvExp4: initial value must be a literal expression`
  - 触发位置:
    - `Runtime/Shaders/Gsplat.shader(905)`
- 当前主假设:
  - Metal/HLSLcc 对这类函数求值或依赖前一个 const 的全局常量初始化更严格
  - 需要把 `exp(-4.0)` 与其归一化倒数改成预计算字面量
- 最强备选解释:
  - 不是 `kGaussianInvExp4` 单独的问题
  - 而是所有全局 `const float = exp(...)` 都会在这条编译链上失败
- 下一步行动:
  - [ ] 修改 `Runtime/Shaders/Gsplat.shader`,把相关常量改成字面量
  - [ ] 同步更新 `Tests/Editor/GsplatShaderKernelTests.cs`
  - [ ] 做最小验证,确认文本契约和数学契约都成立

## 2026-03-12 12:38:06 +0800 Metal shader compile 回归已修复

- 已完成的动作:
  - [x] 修改 `Runtime/Shaders/Gsplat.shader`,将归一化高斯常量改为预计算字面量
  - [x] 同步更新 `Tests/Editor/GsplatShaderKernelTests.cs`
  - [x] 用 Unity MCP 做刷新编译与定向测试验证
- 关键验证结果:
  - Console 未再出现 `kGaussianInvExp4 ... literal expression` 错误
  - `Gsplat.Tests.GsplatShaderKernelTests` 通过 `2/2`
- 当前结论:
  - Metal 回归已经修复
  - Gaussian 边界归一化的数学语义仍保持不变

## 2026-03-12 12:51:02 +0800 新现象: 修改后旧场景里的 `.splat4d` Gaussian 全不显示

- 当前行动目的:
  - 把用户刚反馈的严重回归先证据化
  - 判断它是 shader 编译/变体问题,还是这次归一化核公式确实把可见输出压没了
- 已观察到的事实:
  - 用户反馈:
    - 现在以前场景里的 `.splat4d` Gaussian 都不显示了
  - 当前这还只是现象,还不是根因
- 当前主假设:
  - 归一化核修复虽然通过了最小文本契约和定向测试,但在真实场景参数下把 alpha 压得过低,导致大多数 splat 在 `alpha < 1/255` 处分支被丢弃
- 最强备选解释:
  - 并不是公式数学本身有问题
  - 而是 shader 真实运行路径、关键词变体、材质绑定、或别的门禁让当前场景没有走到预期路径
- 下一步行动:
  - [ ] 先读取 Unity Console,确认有没有新的 shader/material/rendering error
  - [ ] 再回读 `Gsplat.shader` 片元 alpha / discard 路径,做最小数值验证
  - [ ] 必要时做最小回退或改用更稳的对齐公式

## 2026-03-12 13:08:14 +0800 新进展: 回退旧核后 Gaussian 可见性恢复,继续研究更稳的边缘方案

- 当前行动目的:
  - 把本轮真实场景回归的证据收束清楚
  - 在不破坏 Gaussian 可见性的前提下,继续研究 alpha 边缘截断问题
- 已确认的动态事实:
  - 用户反馈:
    - `ParticleDots` 有显示
    - `Gaussian` 完全不显示
  - 在把 shader 回退到旧核后,用户再次确认:
    - 现在显示恢复了
- 当前结论更新:
  - 上一轮“直接照搬 supersplat 的 `normExp`”假设不成立
  - 这条路径已被真实场景动态证据推翻
  - 真实问题不只是片元 alpha 公式本身,还涉及当前实现的 footprint / `A` 定义 / 其它门禁组合
- 下一步行动:
  - [ ] 回写 `notes / ERRORFIX / WORKLOG`,明确旧假设已被推翻
  - [ ] 继续回看 `supersplat` 与当前 `Gsplat.hlsl` 的 footprint / corner 逻辑差异
  - [ ] 设计一个“只修边缘,不打掉整体可见性”的候选方案

## 2026-03-12 14:05:00 +0800 继续排查: 聚焦 alpha 边缘截断,避免再次误改 Gaussian 主核

- 当前行动目的:
  - 用户已确认 Gaussian 显示恢复,现在继续研究“边缘截断感”本身
  - 这次不再把 fragment alpha 公式单独当成根因,而是把 `A` 定义、corner 生成、clip 逻辑与 `.splat4d` 时域权重一起看
- 当前已知事实:
  - 直接移植 `supersplat` 的 `normExp` 已被真实场景动态证据推翻
  - 仅从 base opacity + clip radius 的离线 probe 看,新旧 clip 半径差距不足以单独解释“Gaussian 全不显示”
- 当前主假设:
  - 边缘“截断感”依然可能存在,但真正可安全优化的点,更可能是 `A` 接近边界时的额外 fade 或 clip 口径,而不是整条 Gaussian 核替换
- 最强备选解释:
  - 用户看到的边缘感主要来自 corner footprint / temporalWeight / 预乘混合后的视觉结果,单纯 alpha 核并不是主导项
- 下一步行动:
  - [ ] 补读 `Gsplat.shader` 顶点到片元的完整 `corner -> uv -> A -> alpha` 路径
  - [ ] 对照 supersplat 的 `initCorner / fragment` 全链路,确认 `A` 是否同构
  - [ ] 清理临时 probe 脚本,避免后续误用旧调查工具
  - [ ] 设计一个更保守的最小实验,只在边缘区增加 fade,不动中心能量

## 2026-03-12 14:34:00 +0800 最小实验决策: 采用保守边缘 fade,不再直接移植 `normExp`

- 已完成的证据收集:
  - [x] 补读 `corner -> uv -> A -> alpha` 全链路
  - [x] 对照 supersplat 主 shader,确认它的主路径里未见 `clipCorner(...)`
  - [x] 清理临时 opacity probe 脚本
  - [x] 做保守 edge fade 数值实验
- 当前决策:
  - 不再做“整条 Gaussian 核替换”
  - 改做最小实验:
    - 仅在 `A >= 0.90` 的最外圈额外乘一个 edge fade
    - 中心区(`A < 0.90`)保持旧核完全不变
- 这样做的理由:
  - 静态上避免再次碰 `temporalWeight -> ClipCorner -> A` 这条耦合主链
  - 数值上完整 kernel 仅减少约 `0.42%` 能量
  - 对大多数 clip<0.9 的低 alpha splat 影响近似为 0
- 下一步行动:
  - [ ] 修改 `Runtime/Shaders/Gsplat.shader` 的 Gaussian alpha 路径,加入保守 edge fade helper
  - [ ] 做最小静态/数值验证
  - [ ] 若 Unity MCP session 恢复,补真实场景 before/after 验证

## 2026-03-12 14:37:00 +0800 最小实验已落地,当前等待真实场景验证

- 已完成的动作:
  - [x] 修改 `Runtime/Shaders/Gsplat.shader` 的 Gaussian alpha 路径,加入保守 edge fade helper
  - [x] 做最小静态核对,确认中心区不变且 `A=1 -> alpha=0`
- 静态验证结果:
  - `A=0.90` 时 fade 仍为 `1.0`
  - `A=0.95` 时 fade 为 `0.5`
  - `A=0.99` 时 alpha 已压到 `0.000534`
  - `A=1.00` 时 alpha 为 `0`
- 当前未完成项:
  - [ ] Unity 真实场景 before/after 验证
- 阻塞说明:
  - 当前 Unity MCP 返回 `No Unity instances are currently connected`
  - 因此这一轮还不能把“视觉问题已解决”写成结论

## 2026-03-12 15:13:00 +0800 用户更新: `CameraMode` 已改为全 camera 可见,继续验证 blank 视角是否恢复高斯

- 当前行动目的:
  - 验证之前那张空白截图,到底是不是受相机可见性门禁影响
- 用户刚提供的新事实:
  - `CameraMode` 已被手动改成“全 cam 可见”
- 当前主假设:
  - 之前的 blank 视角至少有一部分是 `CameraMode` / active camera 选择逻辑造成的“该相机不提交 draw”
- 最强备选解释:
  - 即使 CameraMode 放开,空白仍可能来自 shader 顶点阶段的 size/frustum cull,或者目标 transform 并不对应真实 splat 主体中心
- 下一步行动:
  - [ ] 重拍之前的空白定点视角
  - [ ] 对比现在是否已经出现 Gaussian
  - [ ] 若恢复显示,再继续观察边缘观感是否仍有截断感

## 2026-03-12 15:15:00 +0800 动态证据更新: blank 视角在 `CameraMode=AllCameras` 后已恢复 Gaussian

- 已观察到的事实:
  - 使用与之前相同的定点截图参数:
    - `s1_point_cloud_same_source_20260311`
    - `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest`
  - 在用户把 `CameraMode` 改成全 camera 可见后,两张图都已经能看到 Gaussian 内容
- 当前结论更新:
  - 之前的 blank 视角,至少有一部分确实来自 camera 可见性门禁
  - 这条动态证据支持了“不是单纯 shader alpha 核导致 blank”这一判断
- 下一步行动:
  - [ ] 在这两个当前可见的近景视角上,重做 old/new kernel A/B 截图
  - [ ] 观察保守 edge fade 是否只影响外缘,且不再引入不可见回归

## 2026-03-12 15:26:00 +0800 用户要求复验: 旧核 blank 可能是刷新时机问题

- 当前行动目的:
  - 排除“截图时 Unity 尚未完全 ready”这一备选解释
- 当前主假设:
  - 之前旧核下 `s1` 变 blank,有可能是刷新后排序/渲染链还没完全稳定,不是旧核本身导致
- 下一步行动:
  - [ ] 临时切回旧核 helper
  - [ ] 强制 refresh + 轮询 editor ready
  - [ ] 再拍同一 `s1` / `ckpt` 定点视角
  - [ ] 如果旧核也恢复可见,撤回“旧核导致 blank”这条候选解释

## 2026-03-12 15:33:00 +0800 复验完成: 刷新时机问题已证伪旧核 blank 假设,并补完 old/new 像素差分

- 已完成的动作:
  - [x] 临时切回旧核 helper
  - [x] 强制 refresh + 额外等待
  - [x] 重拍 `s1` / `ckpt` 同一定点视角
  - [x] 撤回“旧核导致 blank”这条候选解释
  - [x] 统计真正可比较的 old/new 近景像素差分
- 当前结论:
  - blank 问题至少分成两层:
    1. `CameraMode` / draw submission 门禁
    2. 刷新时机导致的一次性假象
  - 当前保守 edge fade 的真实视觉影响是局部而非整帧级的

## 2026-03-12 15:36:00 +0800 用户要求: 直接 clone 官方仓库到本地做完整对照

- 当前行动目的:
  - 不再依赖网页片段或旧快照
  - 直接拿最新官方仓库源码做完整对照
- 计划动作:
  - [ ] clone `playcanvas/supersplat`
  - [ ] clone `playcanvas/engine`
  - [ ] 从本地完整源码中定位 `gsplat` shader chunk 与 camera / cull / alpha 相关链路

## 2026-03-12 13:49:20 +0800 继续研究: 重新按完整 supersplat/engine 链路对照,不再只盯 fragment alpha

- 当前行动目的:
  - 用户要求把 `playcanvas/supersplat` 重新研究透,不要只凭上一轮局部结论继续改 shader
  - 这次先做完整链路比对,再决定要不要做新的最小实验
- 当前已知事实:
  - 直接移植 `normExp` 的方案已经被真实场景动态证据推翻
  - 当前保守 edge fade 只证明“局部可控”,还没有证明“它就是正确方向”
  - clone 下来的 supersplat / engine 本地源码已经可读
- 当前主假设:
  - 用户看到的边缘截断感,更可能来自 supersplat 与当前实现之间的几何 footprint / `clipCorner` / `minPixelSize` / 动态 alpha 链路差异组合,而不只是 fragment 核函数
- 最强备选解释:
  - 当前 `A` 靠近 1 的 pedestal 仍然是主要观感来源,只是之前因为 camera gate 与刷新时机噪声,把判断搞混了
- 下一步行动:
  - [ ] 从 clone 下来的 `engine` 中确认 `clipCorner`、`initCorner`、`minPixelSize`、`alphaClip` 的真实调用关系
  - [ ] 对照本仓库 `Gsplat.shader` / `Gsplat.hlsl` / `GsplatRendererImpl.cs` 的 `.splat4d` 动态 alpha 链路
  - [ ] 给出一个新的“现象 -> 假设 -> 验证计划 -> 结论”版本
  - [ ] 如果出现高把握候选点,只做一个最小可证伪实验

## 2026-03-12 14:03:10 +0800 候选原因收敛: 先做完整 `normExp` 复验,验证上一轮否定是否仍成立

- 新增静态证据:
  - `ClipCorner` 与 engine 数学上同构
  - `<2px` early-out 在当前默认值下也等价
  - 当前真正大的结构差异,主要落在 `.splat4d` 的 `temporalWeight -> baseAlpha -> ClipCorner` 链路,以及 Gaussian/ParticleDots morph
- 当前主假设:
  - 用户感觉“没太大区别”,更可能是因为当前保守 fade 只影响 `baseAlpha >= 0.1435` 且尤其是 `>= 0.2141` 的 splat
  - 在 `.splat4d` 动态链路下,这部分 splat 占比可能没有想象中高
- 最强备选解释:
  - 即便如此,完整 `normExp` 仍可能在当前现场稳定工作,之前的严重回归主要是 camera gate / refresh 时机噪声
- 下一步行动:
  - [ ] 临时把 shader 切回完整 `normExp`
  - [ ] refresh + 读取 Console
  - [ ] 在当前 `AllCameras` 条件下复拍 `s1` / `ckpt`
  - [ ] 如果未复现严重回归,撤回“完整 `normExp` 已被充分证伪”的口径

## 2026-03-12 14:29:10 +0800 最小复验完成: 重新确认完整 `normExp` 仍会在 `s1` 上触发真实回退

- 已完成的动作:
  - [x] 临时把 shader 切回完整 `normExp`
  - [x] refresh + Console 检查
  - [x] 用 `s1` 的同一定点参数复拍
  - [x] 用临时探针统计 `effectiveAlpha` 分布
  - [x] 将 shader 恢复为保守 edge fade 版本
  - [x] 删除临时探针脚本
- 本轮最关键的结论更新:
  - “完整 `normExp` 之前的严重回归只是 camera gate / refresh 假象” 这条解释不成立
  - 至少对 `s1` 来说,完整 `normExp` 在当前 renderer 上仍会触发真实且巨大的 coverage 回退
- 同时被推翻的次级假设:
  - “主要是 temporalWeight 把 alpha 压低,所以 `normExp` 才坏”
  - 这条对 `s1` 不成立,因为 `s1` 是 `window` 模式,且大多数可见 splat 已达到 `clip=1`
- 当前更强主假设:
  - supersplat 与当前实现的差异,核心不只是 fragment 核函数
  - 更可能还包含 footprint / coverage / 数据标定语义差异
- 当前最强备选解释:
  - 截图链路仍存在额外噪声,导致“回滚后再次截图又 blank”这一新现象尚未分离干净
- 下一步建议方向(本轮先不直接继续改):
  - [ ] 如果继续推进修复,优先研究“footprint/coverage 校准”而不是再次整体替换 fragment 主核
  - [ ] 单独排查为什么本轮回滚后同一 screenshot route 又出现 blank,避免后续验证链继续受噪声污染

## 2026-03-12 14:40:10 +0800 新问题分支: 排查是否是 `ply -> splat4d` 转换链本身导致 coverage / 边缘观感异常

- 当前行动目的:
  - 用户提出一个很关键的新方向: 问题是否来自 `ply -> splat4d` 的转换,而不只是 runtime shader
- 当前已知事实:
  - `s1` 在 `window` 模式且高 alpha splat 占比很高时,完整 `normExp` 仍会导致巨大 coverage 回退
  - 因此单纯 temporal alpha 解释已经不够
- 当前主假设:
  - 转换链如果改写了 opacity / scale / rotation / SH / 时间语义,确实可能放大“尾部能量被拿掉后就填不满表面”的问题
- 最强备选解释:
  - 转换链本身没有明显失真,真正问题仍在当前 renderer 对 `splat4d` 数据的 footprint/coverage 使用方式
- 下一步行动:
  - [ ] 读 `Tools~/Splat4D` 转换脚本
  - [ ] 读 `Editor/GsplatSplat4DImporter.cs` 与相关 importer 读取逻辑
  - [ ] 查 `.ply` 字段到 `GsplatAsset` 的 alpha/scale/time 语义有没有被重编码或量化
  - [ ] 判断“是不是转换问题”目前能否下结论

## 2026-03-12 14:44:20 +0800 用户补充现场资料: `s1` 的源 PLY 路径已知

- 用户提供:
  - `Assets/Gsplat/ply/s1-point_cloud.ply`
- 这条信息非常重要:
  - 现在可以直接对比同一份源 PLY 与现有 `s1_point_cloud_same_source_20260311.splat4d`
  - 不再只是从转换脚本推断可能误差
- 下一步行动:
  - [ ] 读取源 PLY
  - [ ] 与 `s1_point_cloud_same_source_20260311.splat4d` 的 alpha/scale/rotation/position 做逐项比较
  - [ ] 判断转换误差量级是否足以解释当前 coverage 问题

## 2026-03-12 14:48:00 +0800 转换链结论更新: `s1` 的 `ply -> splat4d` 不是当前主因

- 已完成的动作:
  - [x] 用用户提供的 `Assets/Gsplat/ply/s1-point_cloud.ply` 对比当前 `s1_point_cloud_same_source_20260311.splat4d`
- 关键证据:
  - position 误差为 0
  - scale 误差为 0
  - alpha 误差上限约 `0.00196`(`<=1/255`)
  - rotation 量化角误差 < `0.87°`
- 当前结论:
  - 对 `s1` 来说,转换链没有把影响 coverage 的核心几何量(scale/position)改坏
  - 所以“当前 `normExp` 一开就几乎 blank”不能主要归因给 `PLY -> .splat4d` 转换误差
- 仍保留的条件性风险:
  - 若上游 PLY 的 `opacity`/`scale` 语义与脚本参数不匹配(`logit/log` vs `linear/linear`),转换链仍然可能造成严重问题
  - 但这条风险目前没有被 `s1` 样本支持

## 2026-03-12 14:52:30 +0800 新现象: `.ply` 资产无法直接拖放到场景

- 当前行动目的:
  - 用户确认现在支持 `.ply`,但实际在 Unity 里无法把 `.ply` 直接拖进场景
- 当前主假设:
  - 这更像编辑器交互入口缺失,而不是 `.ply` 数据本身解析失败
  - 也就是 importer 可能只生成 `GsplatAsset(ScriptableObject)`,没有生成可实例化的 `GameObject/Prefab`
- 最强备选解释:
  - `.ply` 资产其实导入失败了,只是表面上看起来像普通文件,所以拖放时没有任何可实例化对象
- 下一步行动:
  - [ ] 查 `.ply` importer 是否设置 main object,以及 main object 类型
  - [ ] 查仓库里是否已有从 `GsplatAsset` 创建 `GsplatRenderer` 的菜单或拖放入口
  - [ ] 用 Unity MCP 现场确认 `Assets/Gsplat/ply/s1-point_cloud.ply` 的导入结果

## 2026-03-12 15:18:40 +0800 新问题推进: `.ply` 资产无法拖放到场景中

- 当前行动目的:
  - 用户反馈 `.ply` 不能直接拖进场景,这会直接影响 `.ply` 入口可用性,需要先确认这是导入失败,还是 importer 没有产出可实例化主对象
- 当前主假设:
  - `.ply` importer 目前只产出 `GsplatAsset` 作为子资源,没有像 `.splat4d` importer 那样创建 prefab 并 `SetMainObject`,因此 Inspector 中可见但无法拖进 Hierarchy/Scene
- 最强备选解释:
  - `.ply` 实际导入有错误,或者主对象虽然存在,但不是可实例化对象类型
- 下一步行动:
  - [ ] 对照 `Editor/GsplatImporter.cs` 与 `Editor/GsplatSplat4DImporter.cs`
  - [ ] 用 Unity MCP 确认 `Assets/Gsplat/ply/s1-point_cloud.ply` 的导入资产形态
  - [ ] 如果确认是 importer 入口缺口,补齐 `.ply -> prefab + GsplatRenderer` 主对象生成
  - [ ] refresh 并验证 `.ply` 是否可拖放/实例化

## 2026-03-12 15:28:30 +0800 `.ply` 拖放问题已确认并修复

- 已完成的动作:
  - [x] 对照 `Editor/GsplatImporter.cs` 与 `Editor/GsplatSplat4DImporter.cs`
  - [x] 用 Unity MCP 确认 `Assets/Gsplat/ply/s1-point_cloud.ply` 的导入资产形态
  - [x] 补齐 `.ply -> prefab + GsplatRenderer` 主对象生成
  - [x] bump `ScriptedImporter` version,确保旧 `.ply` 资产自动重导入
  - [x] refresh + compile + Console 检查
  - [x] 跑 EditMode 回归测试 `Gsplat.Tests.GsplatPlyImporterTests.Import_FixturePly_CreatesPlayablePrefabMainObject`
- 关键动态证据:
  - 修复前,`manage_asset search` 显示:
    - `Assets/Gsplat/ply/s1-point_cloud.ply -> assetType = Gsplat.GsplatAsset`
  - 修复并 version bump 后,同一路径显示:
    - `Assets/Gsplat/ply/s1-point_cloud.ply -> assetType = UnityEngine.GameObject`
  - Console: `0` 条新的 error/warning
  - 测试结果:
    - `1 passed, 0 failed`
- 当前结论:
  - `.ply` 之前不能拖进场景,不是因为“不支持 `.ply`”
  - 而是 importer 只把它导入成 `GsplatAsset` 主对象,没有产出可实例化的 main prefab
  - 另外,如果只改 importer 输出逻辑但不 bump version,旧 `.ply` 资产仍会停留在旧导入结果,看起来就像“代码改了但现场没生效”
- 当前状态:
  - `.ply` 导入入口已与 `.splat4d` 基本对齐
  - 用户现在应可直接把 `.ply` 资产拖入场景

## 2026-03-12 15:36:20 +0800 新动态证据: 直接导入 `.ply` 仍有同样的边缘边界感

- 用户新反馈:
  - 直接导入 `.ply` 后,画面结果和 `.splat4d` 一样
  - 高斯边缘仍然存在明显边界感
- 这条证据的重要性:
  - 它进一步削弱了“问题主要来自 `ply -> splat4d` 转换链”的解释
  - 因为 `.ply` 与 `.splat4d` 最终都走进同一套 `GsplatRenderer` 渲染路径,而现在两边现象一致
- 当前主假设更新:
  - 当前明显边界感更可能来自 renderer/shader 的共同逻辑
  - 尤其要优先怀疑 `ClipCorner` / footprint 裁剪 / fragment alpha 这条链
- 最强备选解释:
  - 当前近景大 splat 的观感还混入了排序、曝光、背景对比、超大椭圆 footprint 等因素,并不完全是单一 alpha 核问题
- 下一步行动:
  - [ ] 重新聚焦 `Runtime/Shaders/Gsplat.shader` / `Runtime/Shaders/Gsplat.hlsl`
  - [ ] 设计一个最小可证伪实验,区分“硬边主要来自 fragment alpha”还是“主要来自 ClipCorner / quad 裁剪”
  - [ ] 根据实验结果决定下一轮修复方向

## 2026-03-12 15:48:40 +0800 回滚临时实验: 用户指出当前画面像实验态异常

- 当前行动目的:
  - 刚才为了隔离 `ClipCorner` 影响,临时把它改成 no-op
  - 用户当前看到的夸张画面很可能就是这个实验态,不能拿来代表原始问题
- 立即动作:
  - [ ] 恢复 `ClipCorner(gaussCorner, max(baseAlpha, 1.0 / 255.0))`
  - [ ] refresh + compile
  - [ ] 确认 Console 无新的 shader error

## 2026-03-12 15:58:20 +0800 新的低风险实验: 仅提前 Gaussian 外缘 fade 起点

- 当前行动目的:
  - 在不碰 `ClipCorner`、不恢复 `normExp` 主核的前提下,验证“当前边界感是不是因为外圈 0.6~1.0 区间仍然太亮”
- 依据:
  - 当前核在 `A=0.8` 仍有约 `0.0408 * alpha`
  - `A=0.9` 仍有约 `0.0273 * alpha`
  - 对超大 splat 来说,这足以形成可见轮廓
  - 把 fade 起点从 `0.90` 提前到 `0.60` 时,中心区 `A<=0.60` 完全不变,总能量只下降约 `2.6%`,风险远低于直接上 `normExp`
- 下一步行动:
  - [ ] 仅改 `kGaussianEdgeFadeStart: 0.90 -> 0.60`
  - [ ] refresh + compile
  - [ ] 同机位截图与 baseline 对比

## 2026-03-12 16:02:40 +0800 从稳定基线重来后的阶段结论

- 已完成的动作:
  - [x] 回滚 `ClipCorner no-op` 临时实验
  - [x] 恢复并拍稳定基线截图
  - [x] 只读确认当前 `s1-point_cloud` / `s1_point_cloud_same_source_20260311` 都是 `RenderStyle=0` 的纯 Gaussian
  - [x] 做离线数值分析,确认当前 `fadeStart=0.90` 对外圈压制太晚
  - [x] 执行新的低风险实验: `kGaussianEdgeFadeStart 0.90 -> 0.60`
  - [x] refresh + compile + Console 检查
  - [x] 同机位截图与 baseline 做差分
- 当前结论:
  - 这次从稳定基线重来的结果支持“外圈 alpha 残留过高”这个方向
  - 目前 `fadeStart=0.60` 是比 `normExp` 更安全的候选
- 下一步可继续做:
  - [ ] 用更近的临时拍摄视角观察 `s1` 的局部边缘轮廓
  - [ ] 若 `0.60` 仍偏硬,继续在 `0.60~0.70` 区间微调
  - [ ] 若用户确认当前观感已经更好,再考虑补回归测试或文档说明

## 2026-03-12 16:08:50 +0800 用户指定下一步: 记录 `fadeStart=0.60`,重新试 `normExp`

- 当前行动目的:
  - 按用户要求,把 `fadeStart=0.60` 留作候选记录
  - 重新验证 `normExp` 在当前稳定基线下的真实效果
- 下一步行动:
  - [ ] 用字面量常数恢复 `normExp` 片元核
  - [ ] refresh + compile
  - [ ] 拍 `Main Camera` 对照图
  - [ ] 与恢复基线做差分统计

## 2026-03-12 16:15:20 +0800 `normExp` 重试结论

- 已完成的动作:
  - [x] 切回 `normExp`
  - [x] refresh + compile
  - [x] 拍同机位对照图
  - [x] 与恢复基线做差分统计
- 当前结论:
  - `normExp` 在当前 wide shot 下没有直接 blank
  - 但它的影响比 `fadeStart=0.60` 更广,更像整片 kernel 都被改写
- 下一步动作:
  - [ ] 将工作 shader 恢复到更稳的 `fadeStart=0.60` 候选态
  - [ ] 继续用近景局部验证边界观感

## 2026-03-12 16:21:10 +0800 用户偏好更新: 当前主观观感更喜欢 `normExp`

- 用户新反馈:
  - 当前看起来 `normExp` 更好
  - 因此问题不再是“`normExp` 一定不能用”
  - 真正要查的是: 为什么它以前会在某些情况下像没显示
- 当前主假设:
  - `normExp` 不是全局不可用
  - 更可能是某些 close-up / 巨大 footprint / 稀疏覆盖条件下,可见性会塌得过头
- 下一步行动:
  - [ ] 保持当前 `normExp` 不动
  - [ ] 用临时近景拍摄视角分别观察 `.ply` 与 `.splat4d` 的 `s1`
  - [ ] 判断 close-up 下是否会重现“接近不显示”的现象

## 2026-03-12 16:34:10 +0800 新现象: 当前 `normExp` 版在游戏窗口里明显偏暗

- 用户新反馈:
  - 当前渲染在 Game 窗口里明显暗了,感觉不正常
  - 同时追问: 现在与 supersplat shader 还有哪些差别
- 当前主假设:
  - 这不一定只是“边缘更柔和”的副作用
  - 也可能是当前 `normExp` 对整片 kernel 的 alpha 分布改写过大,导致整体 coverage / 亮度下降
  - 另一条备选解释是: SceneView 与 GameView 本来就存在相机/后处理/曝光差异,图上看到的是两类因素叠加
- 下一步行动:
  - [ ] 本地重新对照 supersplat / playcanvas shader 源码
  - [ ] 列出当前仍然存在的关键差异
  - [ ] 检查当前 GameView 偏暗更像 shader 覆盖率下降,还是相机/后处理差异

## 2026-03-12 16:40:40 +0800 用户要求: 先回到 `normExp` 之前的样子再看

- 采用的合理假设:
  - “以前的样子”先按 `normExp` 之前那版理解
  - 也就是 `fadeStart=0.60` 的 edge-only 候选态
- 下一步行动:
  - [ ] 从当前 `normExp` 回到 `fadeStart=0.60` 版
  - [ ] refresh + compile
  - [ ] 拍同机位图,观察 GameView 是否恢复

## 2026-03-12 16:47:30 +0800 用户反馈: 回到 `fadeStart=0.60` 后仍觉得偏暗

- 这条反馈的重要性:
  - 说明“变暗”不一定只来自 `normExp`
  - 当前连保守 edge fade 候选版也可能仍在压整体 coverage
- 因此下一步不再停留在候选版:
  - [ ] 直接回到真正旧核 `exp(-A*4)`
  - [ ] 不带任何 edge fade
  - [ ] refresh + compile,再看是否恢复亮度

## 2026-03-12 16:52:40 +0800 用户要求: 恢复到这轮实验前的版本

- 已执行:
  - [x] 将 `Runtime/Shaders/Gsplat.shader` 恢复到 `normExp` 之前的保守 edge fade 版本
  - [x] 移除本轮临时加入的 `EvalNormalizedGaussian`
- 当前只做一件事:
  - [ ] refresh + compile,确认恢复动作无新错误

## 2026-03-12 17:08:40 +0800 新主线: 排查 `GameView` 偏暗是否与 shader 无关

- 用户新反馈:
  - 恢复到这轮实验前的 shader 后,GameView 仍然偏暗
  - 因此问题很可能不是这轮 alpha 核改动单独造成的
- 当前主假设:
  - 偏暗可能来自 SceneView / GameView 的相机与曝光差异
  - 尤其当前场景存在 `HDAdditionalCameraData`,要优先检查 HDRP Volume / Exposure / Tone Mapping
- 最强备选解释:
  - Gsplat 自身 premultiplied alpha + 当前资产颜色/SH 分布,本来就整体能量偏低,只是以前没注意到
- 下一步行动:
  - [ ] 读取 Main Camera 与 HDRP camera data
  - [ ] 读取场景 Volume / Exposure / Tone Mapping 设置
  - [ ] 判断“GameView 偏暗”更像相机管线问题,还是 gsplat 自身亮度问题

## 2026-03-12 17:19:10 +0800 `GameView` 偏暗的阶段结论

- 已完成的动作:
  - [x] 读取 Main Camera / HDRP camera data / global volumes
  - [x] 读取高优先级 volume profile 的 Exposure 实际数值
  - [x] 做最小动态实验: `fixedExposure 8.791519 -> 6.0`
  - [x] 拍实验图并恢复原值
- 当前结论:
  - `GameView` 整体偏暗的首要原因,当前强证据指向 HDRP 固定曝光
  - 当前 gsplat shader 不是这条现象的第一嫌疑
- 下一步可选方向:
  - [ ] 如果用户希望画面更亮,就继续在 HDRP Volume 上做 Exposure / Tonemapping 调整
  - [ ] 把 gsplat 边缘问题与 HDRP 曝光问题彻底拆成两条独立问题继续推进

## 2026-03-12 17:23:50 +0800 用户确认亮度主因是 tonemapping,请求恢复 `normExp`

- 用户最新结论:
  - 当前整体偏暗主因已确认是 tonemapping
- 当前行动目的:
  - 不再围绕亮度问题继续排查 shader
  - 只把 `Runtime/Shaders/Gsplat.shader` 恢复到 `normExp` 版本
  - 然后 compile 验证恢复是否成功

## 2026-03-12 16:05:00 +0800 继续调查: 回答“现在和 supersplat shader 差别还有吗”

- 当前行动目的:
  - 把上一轮只做到索引级别的 supersplat 对照补成真正的源码级对照
  - 回答用户当前最关心的问题: 在恢复到 `normExp` 基线后,当前实现和 supersplat 还剩哪些关键差异
- 为什么现在做这个:
  - 用户已经把亮度问题与 tonemapping 区分开
  - 当前问题只剩“shader 本身是否还和 supersplat 有关键分叉”
- 下一步行动:
  - [ ] 读取 `Runtime/Shaders/Gsplat.shader` 与 `Runtime/Shaders/Gsplat.hlsl` 当前相关片段
  - [ ] 读取 `/tmp/gsplat_compare/supersplat` 和 `/tmp/gsplat_compare/engine` 的 gsplat shader 片段
  - [ ] 按“现象 -> 假设 -> 验证计划 -> 结论”整理差异表

## 2026-03-12 16:18:00 +0800 阶段进展

- 已完成 supersplat / engine / 当前仓库的逐文件对照.
- 已确认当前口径:
  - `normExp` 主核已经对齐
  - 但 supersplat forward 不做 `alpha < 1/255` discard
  - supersplat 自定义 vertex 也不做 `clipCorner`
- 当前最强结论:
  - 和 supersplat 还存在关键差异
  - 而且这些差异足以继续影响边缘与 coverage
- 下一步:
  - [ ] 先把本轮对照结论清晰回复给用户
  - [ ] 如果用户要继续逼近 supersplat,优先验证“去掉 forward alpha discard”还是“放松 ClipCorner”哪条更值得先做
