# 笔记: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## 2026-03-12 10:24:00 +0800 来源: 本仓库 `Runtime/Shaders/Gsplat.shader` 初读

### 已观察到的事实

- 当前主 pass 使用:
  - `Blend One OneMinusSrcAlpha`
  - 这是预乘 alpha 路径
- 片元阶段目前不是单一高斯核:
  - `alphaGauss = exp(-A_gauss * 4.0) * i.color.a`
  - `alphaDot = (1.0 - smoothstep(inner2, 1.0, A_dot)) * i.color.a`
  - `alpha = lerp(alphaGauss, alphaDot, styleBlend)`
- 当 `RenderStyle=Gaussian` 时:
  - `styleBlend=0`
  - 理论上只走 `alphaGauss`
- 当前高斯片元还有一个明确的裁切阈值:
  - `if (A_gauss > 1.0 && A_dot > 1.0) discard;`
  - `if (alpha < 1.0 / 255.0) discard;`
- 4D gaussian(timeModel=2) 的时域权重在 vertex 阶段先乘到 `color.w`:
  - `temporalWeight = exp(-0.5 * x * x)`
  - 若 `< _TemporalCutoff` 则整点早退 discard

### 初步判断

- 仅从静态代码看,如果用户观察的是默认 `RenderStyle=Gaussian`,最可疑的不是 `gaussian <-> dot` 过渡本身,而是:
  - 高斯核被硬截断在 `A_gauss <= 1`
  - 再叠加 `alpha < 1/255` 的二次裁切
- 这意味着当前核不是“无限支撑的数学高斯”,而是“有限支持的截断高斯”.
- 这未必是 bug,因为实时 3DGS 实现通常都会做 footprint 截断来控成本。
- 但是否比 `supersplat` 更硬,还需要直接对照它的 shader。

## 2026-03-12 10:27:00 +0800 来源: `playcanvas/supersplat` 初步对照

### 已观察到的事实

- 本仓库 `Runtime/Shaders/Gsplat.hlsl` 文件头已直接注明:
  - `most of these are from https://github.com/playcanvas/engine/tree/main/src/scene/shader-lib/glsl/chunks/gsplat`
- `supersplat` 的主 splat shader 在:
  - `/tmp/supersplat_compare/src/shaders/splat-shader.ts`
- `supersplat` 片元阶段不是直接用 `exp(-A*4.0)` 当最终 alpha,而是先做归一化:
  - `const float EXP4 = exp(-4.0);`
  - `const float INV_EXP4 = 1.0 / (1.0 - EXP4);`
  - `normExp(x) = (exp(x * -4.0) - EXP4) * INV_EXP4`
- `supersplat` 的 discard 条件:
  - `if (A > 1.0) discard;`
  - `if (alpha < 1.0 / 255.0) discard;`
- `supersplat` 默认输出同样是预乘风格:
  - `pcFragColor0 = vec4(color.xyz * alpha, alpha)`

### 当前关键差异假设

- 本仓库当前默认 Gaussian 核:
  - `alphaGauss = exp(-A * 4.0) * i.color.a`
- `supersplat` 默认 Gaussian 核:
  - `alpha = normExp(A) * color.a`
  - 其中 `normExp(1) = 0`
- 这意味着两者最关键的不同不是“有没有截断”,而是“截断边界处是否做归一化并让 alpha 连续收敛到 0”:
  - 当前仓库在 `A=1` 时仍有 `exp(-4) ≈ 0.0183`
  - `supersplat` 在 `A=1` 时会被严格归一到 `0`
- 如果用户主观感觉到“边缘有边界感/不够自然”,这条差异是目前最强候选解释。

## 2026-03-12 10:34:00 +0800 来源: 最小数值验证

### 验证命令

```bash
python3 - <<'PY'
import math
EXP4 = math.exp(-4.0)
INV_EXP4 = 1.0 / (1.0 - EXP4)

def ours(a):
    return 0.0 if a > 1.0 else math.exp(-4.0 * a)

def supersplat(a):
    return 0.0 if a > 1.0 else (math.exp(-4.0 * a) - EXP4) * INV_EXP4

for a in [0.9, 0.95, 0.99, 1.0]:
    print(a, ours(a), supersplat(a))
PY
```

### 关键输出

- `A=0.90`
  - ours: `0.027324`
  - supersplat: `0.009176`
- `A=0.95`
  - ours: `0.022371`
  - supersplat: `0.004131`
- `A=0.99`
  - ours: `0.019063`
  - supersplat: `0.000761`
- `A=1.00`
  - ours: `0.018316`
  - supersplat: `0.000000`
- 边缘环平均 alpha:
  - `A in [0.9, 1.0]`
    - ours: `0.022520`
    - supersplat: `0.004283`
    - 比值: `5.258x`
- 额外阈值判断:
  - discard 阈值是 `1/255 = 0.003922`
  - 当前实现在边界 `A=1` 时仍有 `0.018316`,约为阈值的 `4.67x`
  - 只有 `baseAlpha <= 0.214110` 时,边界 pedestal 才会自然低于 discard 阈值

### 结论候选

- 当前仓库默认 Gaussian 核在边界并不会收敛到 0。
- 对高 opacity splat,边界残留 alpha 足够穿过 discard 阈值,会形成一圈“还有颜色贡献的外沿”。
- 这和用户描述的“边缘有边界感,没有自然过渡到完全不透明/透明的感觉”高度一致。

## 2026-03-12 10:35:00 +0800 来源: RenderStyle 路径确认

### 已观察到的事实

- `Runtime/GsplatRenderer.cs`
  - 默认 `RenderStyle = GsplatRenderStyle.Gaussian`
- `Runtime/GsplatRenderer.cs`
  - 每帧调用 `m_renderer.SetRenderStyleUniforms(blend, dotRadius)`
- `Runtime/GsplatRendererImpl.cs`
  - `SetRenderStyleUniforms` 最终把 `blend01` 写入 shader uniform `_RenderStyleBlend`
- 因此在默认场景下:
  - `styleBlend = 0`
  - 片元阶段实际使用的正是 `alphaGauss = exp(-A * 4.0) * i.color.a`

### 收敛判断

- 用户看到的默认 `.splat4d -> GsplatRenderer` 画面,确实会落到当前这条未归一化的截断高斯核路径上。

## 2026-03-12 11:38:00 +0800 来源: 主工程 Unity MCP 编译与测试验证

### 已验证事实

- 主工程 Unity MCP session 恢复后:
  - `refresh_unity(mode=force, scope=all, compile=request)` 已成功触发编译
  - Console 未出现本次修改引入的新增编译错误
- 定向测试任务:
  - `job_id = 9dfd800b79404af3a49c7574676dbc77`
  - mode: `EditMode`
  - assembly: `Gsplat.Tests.Editor`
  - test filter: `Gsplat.Tests.GsplatShaderKernelTests`
- 测试结果:
  - `total=2`
  - `passed=2`
  - `failed=0`
  - `durationSeconds=0.6364522`
- 通过的用例:
  - `Gsplat.Tests.GsplatShaderKernelTests.NormalizedGaussianKernel_BoundaryConvergesToZero`
  - `Gsplat.Tests.GsplatShaderKernelTests.StandardShader_UsesNormalizedGaussianKernel`

### 当前结论

- 这次 shader 修复已经在主工程真实 Unity session 中通过编译与定向 EditMode 测试验证.
- 当前关于“高斯边缘 pedestal”这条修复链路,证据已经闭环:
  - 静态代码对照
  - 数值验证
  - 主工程 Unity 测试通过

## 2026-03-12 11:39:00 +0800 来源: 临时最小工程旁路验证

### 额外发现

- 为绕开主工程项目锁,曾创建最小临时 Unity 工程仅引用:
  - `com.unity.test-framework`
  - 本地 `wu.yize.gsplat`
- 该临时工程编译失败,报错:
  - `Runtime/GsplatSog4DRuntimeBundle.cs(1448,22): error CS0103: The name ImageConversion does not exist in the current context`

### 推导

- 这说明当前 package 在“脱离主工程环境,单独作为本地包接入”时,至少还隐含依赖了 `com.unity.modules.imageconversion`.
- 主工程之所以没暴露,是因为宿主工程 manifest 本来就带了该模块.
- 这不影响本次高斯边缘修复正确性,但说明 package 自描述依赖仍不完整.

## 2026-03-12 12:26:34 +0800 来源: `ImageConversion` package 依赖缺口修复

### 已观察到的事实

- 当前代码直接使用 `ImageConversion` 的位置包括:
  - `Runtime/GsplatSog4DRuntimeBundle.cs:1448`
  - `Editor/GsplatSog4DImporter.cs:688`
  - `Tests/Editor/GsplatSog4DImporterTests.cs:174`
- 修改前 `package.json` 只声明了:
  - `com.unity.modules.physics`
- 官方 Unity built-in package 文档明确对应关系:
  - `Image Conversion -> com.unity.modules.imageconversion`
  - 且该 built-in package 实现的就是 `ImageConversion` 类

### 本次修改

- 已在 `package.json` 中补入:
  - `com.unity.modules.imageconversion: 1.0.0`
- 修改后位置:
  - `package.json:8`

### 验证

- 使用 `python3` 成功解析 `package.json`
- 断言通过:
  - `dependencies["com.unity.modules.imageconversion"] == "1.0.0"`
- 说明:
  - 本次至少已修复 package 清单层的自描述缺口
  - 主工程原本就能编译,所以这次更重要的价值在于“外部最小工程接入时不再缺模块声明”

## 2026-03-12 12:38:06 +0800 来源: Metal shader compile 回归修复

### 现象

- 用户在 macOS / Metal 下报告 shader 编译错误:
  - `Shader error in Gsplat/Standard: kGaussianInvExp4: initial value must be a literal expression`
- 对应源码位置是上一轮修复新增的全局常量初始化:
  - `Runtime/Shaders/Gsplat.shader:905`

### 当前假设

- Metal/HLSLcc 对全局 `const float` 初始化要求更严格
- 像 `exp(-4.0)` 和 `1.0 / (1.0 - kGaussianExp4)` 这种表达式,在这条编译链上不能作为全局常量初始化

### 修复

- 将 shader 中的两项常量改为预计算字面量:
  - `kGaussianExp4 = 0.0183156389`
  - `kGaussianInvExp4 = 1.0186573604`
- 保持 `EvalNormalizedGaussianAlpha(float a)` 的数学语义不变
- 同步更新 `Tests/Editor/GsplatShaderKernelTests.cs` 的文本契约断言

### 动态验证

- Unity MCP 刷新编译后,Console 未再出现新的 shader compile error
- 仅剩既有无关 warning:
  - `GsplatLidarScanTests.cs` 的 obsolete warning
- 定向测试结果:
  - `job_id = 148d0edbc5dd4b6f80b537e5361080d6`
  - `Gsplat.Tests.GsplatShaderKernelTests`
  - `passed=2, failed=0`

### 结论

- 这次回归不是算法回退
- 而是跨平台 shader 常量初始化口径不兼容
- 当前已通过“字面量常量 + 保留数学契约”的方式修正

## 2026-03-12 13:08:14 +0800 来源: 真实场景证据推翻了“直接移植 `normExp` 即可”的假设

### 现象

- 用户在真实旧场景中直接验证到:
  - `ParticleDots` 有显示
  - `Gaussian` 完全不显示
- 我们随后把 Gaussian shader 回退到旧的 `exp(-A * 4.0)` 路径后
- 用户再次确认:
  - 显示恢复了

### 结论

- 这说明上一轮修复并不是“数值上稍微偏暗”那么简单
- 而是:
  - 在当前仓库这条 Gaussian 渲染链里
  - 直接移植 `supersplat` 的 `normExp(A)`
  - 会在真实场景中破坏可见性
- 因此旧口径必须回滚:
  - “边界 pedestal 是最关键差异”这个判断本身没有被完全推翻
  - 但“只改 fragment alpha 公式就能正确对齐 supersplat”这个修法已经被动态证据推翻

### 当前最强候选方向

- 继续研究:
  - `A` 的定义域是否与 supersplat 完全同构
  - 当前 `InitCorner / ClipCorner` 的 footprint 与对方是否一致
  - 是否应该采用更保守的边缘 fade,而不是一步到位的 `normExp`

## 2026-03-12 14:18:00 +0800 来源: 补读当前 vertex->fragment 链路与 supersplat 静态对照

### 已观察到的事实

- 当前仓库的 Gaussian 路径不是只有 fragment 里的 .
- 在到达 fragment 之前,还有一条更关键的顶点链路:
  -  时,先把  乘到 
  - 
  - 
  - 然后才把  送进 fragment 计算 
- 因此当前  的定义域,会被  通过  间接改写.
-  的主 shader  中:
  - vertex 确实调用了 
  - fragment 确实使用了 
  - 但主 shader 里没有看到对  的调用
- 这说明当前仓库与 supersplat 的差异,至少不止一处:
  - 片元 alpha 核不同
  - 顶点阶段几何裁剪口径也可能不同

### 当前判断

- 这进一步削弱了“只替换 fragment alpha 就能对齐 supersplat”的成立条件.
- 在当前仓库里, 是一条耦合链.
- 只改最后一段  而不同时对齐前面的 footprint 口径,很可能会得到和 supersplat 完全不同的有效覆盖结果.

## 2026-03-12 14:22:00 +0800 来源: 保守边缘 fade 的最小数值实验

### 验证命令



### 关键输出

- 对完整 unit kernel()的面积平均积分:
  - 旧核: 
  - : 
  - 保守 fade 起点  时: 
  - 相对旧核只减少: 
- 对 probe 中常见的 clip 半径:
  -  / 
    - fade 起点设到  后,积分比仍是 
    - 说明这类被  缩小的 splat 完全不受影响
  - 
    -  起 fade 时,积分比仍有 
    - 外缘 alpha 从  降到 
  - 
    -  起 fade 时,积分比 
    - 边界  从  收敛到 

### 当前结论候选

- 如果只在  的最外圈,例如 ,额外乘一个 :
  - 对大多数已经被  缩小的低 alpha splat,几乎没有影响
  - 对 clip 接近 1 的高 alpha splat,能把最后一圈 pedestal 压下去
  - 从数值上看,这比直接改成  保守得多
- 这还只是静态数值证据.
- 在拿到真实场景 before/after 前,它仍然只能算“更安全的候选方案”,不能直接写成已确认根因.

## 2026-03-12 14:29:00 +0800 更正记录: 14:18 / 14:22 两段笔记在追加时被 shell 展开污染,以下内容为有效覆盖版本

### 覆盖说明

- 上一条 14:18 / 14:22 记录因为错误使用 heredoc,正文里的反引号被 shell 展开,内容已损坏.
- 按本仓库 append-only 规则,不回改旧段落.
- 从这一条开始,以这里的内容作为有效版本.

### 已观察到的事实

- 当前仓库的 Gaussian 路径不是只有 fragment 里的 `exp(-A * 4.0)`.
- 在到达 fragment 之前,还有一条更关键的顶点链路:
  - `.splat4d` 且 `timeModel=2` 时,先把 `temporalWeight = exp(-0.5 * x * x)` 乘到 `color.w`
  - 然后得到 `baseAlpha = color.w`
  - 再执行 `ClipCorner(gaussCorner, max(baseAlpha, 1.0 / 255.0))`
  - 最后才把 `corner.uv` 送进 fragment 计算 `A_gauss = dot(uv, uv)`
- 因此当前 `A_gauss` 的定义域,会被 `baseAlpha` 通过 `ClipCorner` 间接改写.
- `supersplat` 的主 shader `src/shaders/splat-shader.ts` 中:
  - vertex 确实调用了 `initCorner(source, center, corner)`
  - fragment 确实使用了 `normExp(A)`
  - 但主 shader 里没有看到对 `clipCorner(...)` 的调用
- 这说明当前仓库与 supersplat 的差异,至少不止一处:
  - 片元 alpha 核不同
  - 顶点阶段几何裁剪口径也可能不同

### 当前判断

- 这进一步削弱了“只替换 fragment alpha 就能对齐 supersplat”的成立条件.
- 在当前仓库里,`temporalWeight -> baseAlpha -> ClipCorner -> A_gauss -> alphaGauss` 是一条耦合链.
- 只改最后一段 `alphaGauss` 而不同时对齐前面的 footprint 口径,很可能会得到和 supersplat 完全不同的有效覆盖结果.

### 保守边缘 fade 的最小数值实验

- 验证脚本核心思路:
  - 保持旧核 `exp(-4A)` 不变
  - 仅在最外圈附加 `edgeFade = 1 - smoothstep(start, 1.0, A)`
  - 观察不同 `start` 对积分能量和外缘 alpha 的影响
- 关键输出:
  - 对完整 unit kernel(`A in [0,1]`)的面积平均积分:
    - 旧核: `0.245421`
    - `normExp`: `0.231343`
    - 保守 fade 起点 `A=0.90` 时: `0.244385`
    - 相对旧核只减少: `0.422%`
  - 对 probe 中常见的 clip 半径:
    - `clip=0.4108` / `0.8211`
      - fade 起点设到 `A=0.90` 后,积分比仍是 `1.000000`
      - 说明这类被 `ClipCorner` 缩小的 splat 完全不受影响
    - `clip=0.9718`
      - `A=0.90` 起 fade 时,积分比仍有 `0.999331`
      - 外缘 alpha 从 `0.022878` 降到 `0.013354`
    - `clip=1.0000`
      - `A=0.90` 起 fade 时,积分比 `0.995778`
      - 边界 `A=1` 从 `0.018316` 收敛到 `0`

### 当前结论候选

- 如果只在 `A` 的最外圈,例如 `A >= 0.90`,额外乘一个 `1 - smoothstep(0.90, 1.0, A)`:
  - 对大多数已经被 `ClipCorner` 缩小的低 alpha splat,几乎没有影响
  - 对 clip 接近 1 的高 alpha splat,能把最后一圈 pedestal 压下去
  - 从数值上看,这比直接改成 `normExp` 保守得多
- 这还只是静态数值证据.
- 在拿到真实场景 before/after 前,它仍然只能算“更安全的候选方案”,不能直接写成已确认根因.

## 2026-03-12 14:37:00 +0800 来源: 保守 edge fade 已落地到 shader

### 本次修改

- 文件: `Runtime/Shaders/Gsplat.shader`
- 新增 helper:
  - `EvalConservativeGaussianEdgeFade(float a)`
  - 公式: `1 - smoothstep(0.90, 1.0, a)`
- Gaussian alpha 从:
  - `exp(-A_gauss * 4.0) * i.color.a`
- 改为:
  - `exp(-A_gauss * 4.0) * EvalConservativeGaussianEdgeFade(A_gauss) * i.color.a`

### 最小静态验证

- `A=0.00`
  - fade=`1.000000`, alpha=`1.000000`
- `A=0.50`
  - fade=`1.000000`, alpha=`0.135335`
- `A=0.90`
  - fade=`1.000000`, alpha=`0.027324`
- `A=0.95`
  - fade=`0.500000`, alpha=`0.011185`
- `A=0.99`
  - fade=`0.028000`, alpha=`0.000534`
- `A=1.00`
  - fade=`0.000000`, alpha=`0.000000`

### 当前结论

- 这个改动满足“只软边,不改中心能量”的设计目标.
- 但它目前还只有静态与数值证据.
- 在真实 `.splat4d` 场景重新截图前,还不能确认主观观感是否已经足够改善.

## 2026-03-12 15:23:00 +0800 来源: 用户把 `CameraMode` 改成全 camera 可见后的复验

### 现象

- 之前我用同一组定点截图参数时,出现过 blank 图:
  - `look_at = s1_point_cloud_same_source_20260311`
  - `view_position = [-6.44581, 0.63754, 9.6]`
- 用户随后明确告知:
  - `CameraMode` 已改成“全 cam 可见”
- 在不改定点截图参数的前提下重新截图:
  - `Assets/Screenshots/s1_close_probe_allcams.png`
  - `Assets/Screenshots/ckpt_close_probe_allcams.png`
- 两张图都已经出现了 Gaussian 内容

### 当前判断

- 这是一条强动态证据:
  - 之前的 blank 视角,至少有一部分确实来自 `CameraMode` / active camera 门禁
- 因此“blank 图”不能再被简单归因成:
  - 纯 shader alpha 问题
  - 或纯 fragment 核问题
- 更合理的分层是:
  - `CameraMode` 决定“该相机有没有 draw submission”
  - shader 边缘公式再决定“draw 之后边界看起来硬不硬”

### 结论更新

- 当前已经可以把两个问题拆开:
  1. blank / 完全不可见
     - 至少部分与 camera 可见性门禁相关
  2. 边缘截断感
     - 才是这条 shader edge fade 要解决的下一层问题

## 2026-03-12 15:31:00 +0800 来源: 用户提醒“可能刚才没刷新”后的旧核复验

### 验证流程

- 临时把 `EvalConservativeGaussianEdgeFade` 改成 `return 1.0`,让 shader 回到旧核等价态
- 这次不再急着截图,而是:
  - `refresh_unity(mode=force, compile=request)`
  - 额外等待数秒
  - 确认 Console 无新增 error/warning
  - 再拍同一组定点视角

### 关键结果

- 旧核复验图:
  - `Assets/Screenshots/s1_close_old_kernel_allcams_waited.png`
  - `Assets/Screenshots/ckpt_close_old_kernel_allcams_waited.png`
- 两张图都恢复了 Gaussian 内容
- 这说明:
  - 之前“旧核下 s1 变 blank”的那次结果不可靠
  - 用户提醒的方向是对的,更合理的解释是刷新/稳定时机问题

### 结论更新

- 需要明确撤回的候选解释:
  - “旧核在该视角下会导致 s1 blank” 这个判断不成立
- 推翻它的证据:
  - 在更严格的刷新后复验里,同一旧核、同一定点视角已经恢复可见

## 2026-03-12 15:33:00 +0800 来源: 真正可比较的 old/new 近景差分统计

### 对比对象

- `s1_close_old_kernel_allcams_waited.png`
  vs
  `s1_close_conservative_allcams_rerun.png`
- `ckpt_close_old_kernel_allcams_waited.png`
  vs
  `ckpt_close_conservative_allcams_rerun.png`

### 像素统计

- `s1`
  - `mean_rgb_abs_sum = 0.2419`
  - `diff_pixels_gt1 = 41403 (5.111481%)`
  - `diff_pixels_gt5 = 3453 (0.426296%)`
- `ckpt`
  - `mean_rgb_abs_sum = 0.4552`
  - `diff_pixels_gt1 = 92919 (11.471481%)`
  - `diff_pixels_gt5 = 3425 (0.422840%)`

### 当前结论

- 如果按更有意义的阈值 `RGB abs sum > 5` 看,
  - 两个近景视角的 old/new 差异都约为整帧的 `0.42%`
- 这和之前的数值预期一致:
  - 保守 edge fade 没有带来“大面积亮度回退”
  - 它更像只动了外缘的一小圈像素

## 2026-03-12 14:02:30 +0800 来源: clone 下来的 engine 与当前实现逐段对照

### 已确认的同构点

- `ClipCorner` 数学上与 playcanvas/engine 是同构的:
  - engine: `sqrt(log(255.0 * alpha)) * 0.5`
  - 当前 HLSL: `sqrt(-log(1.0 / 255.0 / alpha)) / 2.0`
  - 两者代数等价
- `InitCorner` 的 `<2px` 早退,在当前默认口径下也与 engine 等价:
  - engine: `if (max(l1, l2) < minPixelSize)` 且默认 `minPixelSize=2.0`
  - 当前: `if (l1 < 2.0 && l2 < 2.0)`
  - 当 `minPixelSize=2.0` 时,两者判定结果一致

### 已确认的关键差异

- engine 是纯 Gaussian 主路径:
  - 顶点里总是 `clipCorner(corner, clr.w)`
  - 片元里总是 `alpha = normExp(A) * gaussianColor.a`
- 当前仓库不是纯 Gaussian 主路径,而是混合链路:
  - `temporalWeight -> baseAlpha -> ClipCorner -> corner morph(Gaussian <-> ParticleDots) -> alphaGauss/alphaDot lerp`
- 当前仓库把 `.splat4d` 的时间权重先乘进 `color.w`,再拿 `baseAlpha` 去驱动 `ClipCorner`
  - 这一步在 playcanvas/engine 里不存在
- 当前仓库的保守 edge fade 只在 `A >= 0.90` 才开始工作

### 数学小结: 为什么当前保守 fade 可能看起来“没太大区别”

- `ClipCorner` 会把 quad 的可见 A 上限压到 `Amax = clip^2`
- 只有当 `Amax >= 0.90` 时,当前保守 fade 才有机会生效
- 由 `clip = min(1, sqrt(log(255*alpha))/2)` 可得:
  - 要让 `Amax >= 0.90`,需要 `alpha >= exp(3.6) / 255 ≈ 0.143522`
  - 要让 `clip = 1` 并真正跑到 `A = 1`,需要 `alpha >= exp(4.0) / 255 ≈ 0.214110`
- 这意味着:
  - 当 `baseAlpha < 0.1435` 时,当前保守 fade 完全不会触发
  - 当 `0.1435 <= baseAlpha < 0.2141` 时,会稍微触发,但远比 engine 的整条 `normExp` 弱
  - 当 `baseAlpha >= 0.2141` 时,才会真正出现“边界从 pedestal 收到 0”的强差异

### 用具体数值看边界 alpha

- `baseAlpha = 1.0`
  - old kernel 边界: `0.018316`
  - engine `normExp` 边界: `0.0`
  - 当前 conservative fade 边界: `0.0`
- `baseAlpha = 0.15`
  - `Amax ≈ 0.911`
  - old kernel 边界: `0.003922`
  - engine `normExp` 边界: `0.001196`
  - 当前 conservative fade 边界: `0.003789`
- `baseAlpha = 0.10`
  - `Amax ≈ 0.810`
  - old kernel 边界: `0.003922`
  - engine `normExp` 边界: `0.002129`
  - 当前 conservative fade 边界: `0.003922`

### 当前判断

- “当前保守 fade 没太大区别”这件事,从数学上是说得通的,不是错觉:
  - 它只动高 alpha splat 的最后一圈
  - 而 `.splat4d` 路径里 `temporalWeight` 又会继续把一部分 splat 的 `baseAlpha` 往下压
- 这还不能直接证明“真正根因就是 temporalWeight + ClipCorner”
- 但它已经足够支持下一步最小实验:
  - 在现在 `CameraMode=AllCameras` 且 refresh 稳定的条件下,重新验证完整 `normExp` 是否真的会再次出现严重回归

## 2026-03-12 14:18:40 +0800 来源: 在当前 `AllCameras + refresh` 条件下重跑完整 `normExp`

### 现象

- 我把 `Runtime/Shaders/Gsplat.shader` 临时切回完整 `normExp`
- Unity refresh 后,Console 没有新的 shader error/warning
- 用之前已经验证过的同一组 `s1` 定点参数再次截图:
  - `look_at = s1_point_cloud_same_source_20260311`
  - `view_position = [-6.44581, 0.63754, 9.6]`
- 新图:
  - `Assets/Screenshots/s1_close_normexp_allcams_rerun_1200.png`
- 对照旧图:
  - `Assets/Screenshots/s1_close_old_kernel_allcams_waited.png`
  - `Assets/Screenshots/s1_close_conservative_allcams_rerun.png`
- 结果:
  - old / conservative 都能看到室内重建内容
  - 完整 `normExp` 下同视角又回到了几乎 blank 的状态

### 像素差分

- `s1 old vs normexp`
  - `mean_rgb_abs_sum = 75.8529`
  - `diff_pixels_gt1 = 708100 (87.419753%)`
  - `diff_pixels_gt5 = 504086 (62.232840%)`
  - `diff_pixels_gt20 = 482215 (59.532716%)`
- `s1 conservative vs normexp`
  - `mean_rgb_abs_sum = 75.8857`
  - `diff_pixels_gt1 = 704936 (87.029136%)`
  - `diff_pixels_gt5 = 502983 (62.096667%)`
  - `diff_pixels_gt20 = 482178 (59.528148%)`

### 当前结论

- 这次动态证据比上一轮更强:
  - 即使在 `CameraMode=AllCameras`
  - 且 refresh/compile 已完成
  - 完整 `normExp` 仍会在 `s1` 这个定点视角下引发极大可见性回退
- 因此现在已经不能把“之前的严重回归只是 camera gate / refresh 假象”当成成立解释
- 更准确的口径应该是:
  - camera gate / refresh 噪声确实存在
  - 但完整 `normExp` 在当前 renderer / 当前资产上也确实能触发真实回退

## 2026-03-12 14:21:10 +0800 来源: 临时 effective alpha 探针

### 目的

- 不再只猜“保守 fade 为什么没太大区别”
- 直接统计当前场景里两个目标 renderer 在当前 `TimeNormalized` 下的 `effectiveAlpha` 分布

### `s1_point_cloud_same_source_20260311`

- Asset: `Assets/Gsplat/splat/s1_point_cloud_same_source_20260311.splat4d`
- `Has4D=True`, `TimeModel=window`, `TimeNormalized=0.000000`
- `RawAlpha>0 = 169133 (100.0000%)`
- `VisibleAfterTemporal = 169133 (100.0000%)`
- `EffectiveAlpha >= 1/255 = 169133 (100.0000% of visible)`
- `EffectiveAlpha >= 0.143522(Amax>=0.9) = 165299 (97.7331% of visible)`
- `EffectiveAlpha >= 0.214110(clip=1) = 158686 (93.8232% of visible)`
- `EffectiveAlpha mean = 0.736033`, `minVisible = 0.019608`, `max = 1.000000`

### `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest`

- Asset: `Assets/Gsplat/splat/v2/ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest.splat4d`
- `Has4D=True`, `TimeModel=gaussian`, `TimeNormalized=0.706928`
- `RawAlpha>0 = 1335022 (99.9918%)`
- `VisibleAfterTemporal = 730750 (54.7325%)`
- `EffectiveAlpha >= 1/255 = 610643 (83.5639% of visible)`
- `EffectiveAlpha >= 0.143522(Amax>=0.9) = 213942 (29.2770% of visible)`
- `EffectiveAlpha >= 0.214110(clip=1) = 171878 (23.5208% of visible)`
- `EffectiveAlpha mean = 0.166708`, `minVisible = 0.000039`, `max = 1.000000`

### 这组探针推翻和支持了什么

- 被推翻的主假设:
  - “完整 `normExp` 的严重回退主要是 `.splat4d` 的 temporalWeight 把 alpha 压低导致”
  - 对 `s1` 不成立
  - 因为 `s1` 是 `window` 模式,并且可见 splat 中有 `93.8%` 已经达到 `clip=1`
- 仍然成立的部分解释:
  - `ckpt` 这类 `gaussian timeModel` 资产里,temporalWeight 的确会让很多 splat 进不到 `A>=0.9` 区域
  - 这能解释为什么当前保守 fade 在 `ckpt` 上更容易显得 subtle
- 当前更强的候选判断:
  - 对 `s1` 而言,问题已经不再是“有没有足够多的高 alpha splat”
  - 而是我们当前 renderer 的 footprint / coverage 语义与 supersplat 仍有更深的结构差异
  - 直接把 fragment 主核改成 `normExp` 会让这种差异暴露成大面积 coverage 回退

## 2026-03-12 14:28:20 +0800 来源: 实验清理后的回滚确认

### 已完成的动作

- 已将 `Runtime/Shaders/Gsplat.shader` 恢复为保守 edge fade 版本
- 已删除临时脚本 `Assets/Editor/GsplatEffectiveAlphaProbe.cs`
- refresh 后 Console 无新的 shader error/warning

### 新现象

- 在本轮实验结束后,我再次用同一组 `s1` 定点参数重拍保守版本:
  - `Assets/Screenshots/s1_close_conservative_post_revert_1200.png`
  - `Assets/Screenshots/s1_close_conservative_post_revert_1200_waited.png`
- 这两张图又都变成了 blank

### 当前处理口径

- 这条新现象目前不能直接拿来推翻更早的 old/conservative 可见截图
- 因为这个项目已经反复证明过:
  - 相机可见性门禁
  - refresh 时机
  - 截图链路状态
  都会让“同一 shader,同一视角”的结果出现噪声
- 因此目前更稳的做法是:
  - 把“完整 `normExp` 在 `s1` 下会触发严重回退”视为已由强动态证据支持
  - 把“回滚后当前这两次截图为何又 blank”视为新的待分离噪声,暂不拿来重写主结论

## 2026-03-12 14:47:20 +0800 来源: 用用户提供的源 `Assets/Gsplat/ply/s1-point_cloud.ply` 直接对比现有 `s1_point_cloud_same_source_20260311.splat4d`

### 对比对象

- 源 PLY:
  - `Assets/Gsplat/ply/s1-point_cloud.ply`
- 现有 `.splat4d`:
  - `Assets/Gsplat/splat/s1_point_cloud_same_source_20260311.splat4d`

### 先确认文件形态

- `s1_point_cloud_same_source_20260311.splat4d` 不是 v2 headered 文件
- 文件头不是 `SPL4DV02`,说明它是 v1 record 流
- 记录数:
  - `10824512 / 64 = 169133`
- 与场景里 renderer 的 `SplatCount=169133` 一致
- 文件内 4D 字段:
  - `time` 全为 `0`
  - `duration` 全为 `1`
  - `velocity` 全为 `0`
- 这与转换脚本在“单个 PLY / average 模式”下的输出完全一致

### 逐项误差结果

- `position L2 error`
  - `mean = 0`
  - `max = 0`
- `scale abs error`
  - `mean = 0`
  - `max = 0`
- `alpha abs error`
  - `mean = 0.0009771170`
  - `max = 0.0019608140`
  - 量级基本就是 `1/255`
- `rotation angular error`
  - `mean = 0.4285°`
  - `max = 0.8695°`
- `baseRgb abs error`
  - 有明显量化误差
  - 但它主要影响颜色/SH0 近似,不直接决定 coverage

### 当前结论

- 对 `s1` 这个真实样本来说:
  - `ply -> splat4d` 转换没有改动 position
  - 也没有改动 scale
  - opacity 只引入了 `<= 1/255` 的量化误差
  - rotation 只有不到 1 度的角度量化误差
- 因此如果问题问的是:
  - “当前 `s1` 上完整 `normExp` 一开就几乎 blank,是不是因为 PLY 转 `.splat4d` 时把 alpha/scale 变坏了?”
- 当前证据支持的回答是:
  - 不是主要原因
  - 至少对 `s1` 不是

### 但转换链仍然有两个真实风险点

- 风险 1: 参数语义配错
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py` 默认假设:
    - `--opacity-mode=logit`
    - `--scale-mode=log`
  - 如果某个上游 PLY 实际已经是线性 alpha / 线性 scale,
    再错误套一次 `sigmoid/exp`,那就真的会把 coverage 搞坏
- 风险 2: v1 转换会丢高阶 SH,并量化 alpha/color/rotation
  - 对颜色与材质观感会有影响
  - 但按当前 `s1` 的对比结果,这不足以解释“完整 `normExp` 导致的巨大 coverage 崩塌”

## 2026-03-12 15:28:30 +0800 来源: `.ply` 无法拖入场景 的静态 + 动态排查

### 现象

- 用户反馈: `.ply` 文件现在虽然能被识别,但无法直接拖进场景

### 假设

- 主假设:
  - `.ply` importer 只生成了 `GsplatAsset` 这个 `ScriptableObject` 子资源
  - 没有像 `.splat4d` importer 一样创建 `GameObject + GsplatRenderer` 并 `SetMainObject`
- 备选解释:
  - `.ply` 实际导入失败,或者主对象已经存在但不是可实例化类型

### 验证计划

1. 静态对照 importer:
   - 查 `Editor/GsplatImporter.cs`
   - 查 `Editor/GsplatSplat4DImporter.cs`
2. 动态确认 Unity 当前导入结果:
   - 用 Unity MCP `manage_asset search`
3. 如果主假设成立:
   - 给 `.ply` importer 补 `prefab + GsplatRenderer + SetMainObject`
   - 再补 `ScriptedImporter` version bump
4. 验证:
   - refresh + compile
   - 再查 `assetType`
   - 跑 EditMode 测试

### 静态证据

- `Editor/GsplatImporter.cs`
  - 只有 `ctx.AddObjectToAsset("gsplatAsset", gsplatAsset)`
  - 原先没有 `ctx.SetMainObject(...)`
  - 原先没有 prefab / `GsplatRenderer`
- `Editor/GsplatSplat4DImporter.cs`
  - 有 `var prefab = new GameObject(...)`
  - 有 `var renderer = prefab.AddComponent<GsplatRenderer>()`
  - 有 `ctx.AddObjectToAsset("prefab", prefab)`
  - 有 `ctx.SetMainObject(prefab)`

### 动态证据

- 修复前:
  - `Assets/Gsplat/ply/s1-point_cloud.ply -> assetType = Gsplat.GsplatAsset`
- 修复后(并 bump importer version 后):
  - `Assets/Gsplat/ply/s1-point_cloud.ply -> assetType = UnityEngine.GameObject`
- Console:
  - refresh/compile 后没有新的 error/warning
- EditMode 测试:
  - `Gsplat.Tests.GsplatPlyImporterTests.Import_FixturePly_CreatesPlayablePrefabMainObject`
  - 结果: `Passed`

### 结论

- 主假设成立
- `.ply` 不能拖进场景的直接原因是:
  - 导入主对象类型错误
  - 不是 `GameObject`,而是 `GsplatAsset`
- 另一个容易遗漏的次级问题也被验证了:
  - 只改 importer 逻辑不够
  - 必须 bump `ScriptedImporter` version,否则已有 `.ply` 资产不会自动重导入,现场会继续保留旧主对象类型

## 2026-03-12 15:36:20 +0800 来源: 用户现场反馈 `.ply` 结果与 `.splat4d` 一样都有明显边界感

### 现象

- 用户给出最新现场结果:
  - 直接导入 `.ply`
  - 最终观感与 `.splat4d` 一样
  - 边缘依然有明显边界感,不是平滑过渡到完全透明

### 当前意义

- 这不是最终根因结论
- 但它是一条很强的新动态证据:
  - 它说明“格式转换链是主因”这条解释继续变弱
  - 因为 `.ply` 并没有经过 `ply -> splat4d` 二次打包,但症状仍在

### 当前更合理的怀疑方向

- 共同渲染路径:
  - `Runtime/Shaders/Gsplat.shader`
  - `Runtime/Shaders/Gsplat.hlsl`
  - `GsplatRendererImpl`
- 更具体地说,要优先拆分两类可能性:
  1. fragment alpha 核太硬
  2. `ClipCorner` / corner shrink / quad footprint 裁剪造成了几何边界感

### 当前结论口径

- 已验证结论:
  - `.ply` 和 `.splat4d` 都会出现同类边缘边界感
- 候选假设:
  - 问题主要在共同的 renderer/shader 路径
  - 但尚未验证“究竟是 fragment alpha 还是 ClipCorner 为主”

## 2026-03-12 15:44:40 +0800 来源: 最小实验 - 让 `ClipCorner` 退化为 no-op,观察同机位截图变化

### 实验目标

- 区分“明显边界感”主要来自哪一层:
  1. `ClipCorner` 几何裁剪
  2. fragment alpha 核

### 实验方法

- 仅改一处:
  - 把 `ClipCorner(gaussCorner, max(baseAlpha, 1.0 / 255.0))`
  - 临时改成 `ClipCorner(gaussCorner, 255.0)`
- 这样 `clip = 1`,等价于让 `ClipCorner` 失效
- 其余逻辑保持不变:
  - `exp(-A*4)` 保持不变
  - 当前保守 edge fade 保持不变
  - blend / discard / styleBlend 都不变
- 用同一相机 `Main Camera` 拍 before/after:
  - `Assets/Screenshots/ply_edge_clipcorner_baseline.png`
  - `Assets/Screenshots/ply_edge_clipcorner_disabled.png`

### 动态证据

- Console:
  - 无新的 shader compile error
- 差分统计:
  - `diff_pixels_gt16 = 4.0354%`
  - `diff_pixels_gt32 = 0%`
  - `mean_rgba ~= [2.13, 2.09, 1.94, 0]`
- 增强 diff 图:
  - `Assets/Screenshots/ply_edge_clipcorner_diff_boost.png`
- 观察结果:
  - 画面确实有变化
  - 但不是“根本反转”级别的变化
  - 主要变化集中在少数超大椭圆 splat 区域,不是全局边界感都被消掉

### 当前结论

- 这条实验不支持“明显边界感主要完全由 `ClipCorner` 造成”
- 更准确地说:
  - `ClipCorner` 会影响部分超大 splat 的 footprint
  - 但它不像是当前问题的唯一主因
- 下一轮更值得怀疑的方向:
  - fragment alpha 外缘衰减仍然太弱
  - 或 blend / premultiplied 输出与当前超大 footprint 叠加后,把外圈 pedestal 放大成可感知轮廓

## 2026-03-12 16:02:40 +0800 来源: 从稳定基线重来后,只提前外缘 fade 起点到 `0.60`

### 先恢复稳定基线

- 已撤回刚才的 `ClipCorner no-op` 临时实验
- restore 后重新 compile
- Console: `0` 条新的 error/warning
- 稳定基线截图:
  - `Assets/Screenshots/ply_edge_restored_baseline.png`

### 新的主假设

- 当前问题的主因更像是“外圈还太亮”,不是 `ClipCorner` 本身
- 数值证据:
  - 现有版本(`fadeStart=0.90`)在
    - `A=0.8` 时仍有 `0.040762 * alpha`
    - `A=0.9` 时仍有 `0.027324 * alpha`
  - 对超大椭圆 splat,这足以形成可感知轮廓

### 为什么先试 `0.60`

- `fadeStart=0.60` 时:
  - `A<=0.60` 的中心区完全不变
  - 总能量相对当前版本只下降约 `2.6%`
  - 风险远低于把整条主核替换成 `normExp`
- 对比 `normExp`:
  - 在 `A=0.8` 左右,压边能力接近
  - 但不会像 `normExp` 那样同时把中部能量一起明显压低

### 动态验证

- 修改:
  - `kGaussianEdgeFadeStart: 0.90 -> 0.60`
- compile 后 Console:
  - 无新的 shader error
- 新截图:
  - `Assets/Screenshots/ply_edge_fadestart_060.png`
- 与恢复基线的差分统计:
  - `diff_pixels_gt16 = 0.2777%`
  - `diff_pixels_gt32 = 0.0003%`
  - 说明没有出现大面积重度回退
- 增强 diff 图:
  - `Assets/Screenshots/ply_edge_fadestart_060_diff_boost.png`
- 观察:
  - 变化主要集中在高斯外缘的弧形边界区域
  - 没出现此前 `normExp` 那种“整体接近 blank”的严重回退

### 当前结论

- 这条实验支持新的主假设:
  - 当前明显边界感的更大来源是 fragment 外圈残留 alpha 偏高
  - 不是 `ClipCorner` 单独主导
- `fadeStart=0.60` 目前是一个更安全的候选方向
- 但是否已经“够好”,仍需要继续看近景局部轮廓

## 2026-03-12 16:08:50 +0800 用户决策: 当前 `fadeStart=0.60` 先记为候选,接着重新试 `normExp`

### 用户反馈

- 用户明确指出:
  - 当前这条“提前 fade 起点”的方案,alpha 是否正确还不确定
  - 先把它记录下来
  - 然后重新试一次 `normExp`

### 当前执行策略

- 保留以下事实记录:
  - `fadeStart=0.60` 是当前一个更安全的候选
  - 它没有出现明显 blank 回退
  - 但它还不是已确认正确答案
- 下一步:
  - 恢复到 `normExp` 片元核
  - 保持当前 `ClipCorner` 为原始逻辑
  - 再做同机位截图对照

## 2026-03-12 16:15:20 +0800 来源: 按用户要求重新试 `normExp`

### 实验前提

- 从稳定基线出发
- `ClipCorner` 已恢复原始逻辑
- 重新把 fragment Gaussian 核切回 `normExp`
- 使用 Metal 可编译的字面量常数:
  - `0.0183156389`
  - `1.0186573604`

### 动态验证

- compile:
  - Console 无新的 error/warning
- 截图:
  - `Assets/Screenshots/ply_edge_normexp_retry.png`
- 与恢复基线的差分统计:
  - `diff_pixels_gt1 = 45.0114%`
  - `diff_pixels_gt4 = 23.3280%`
  - `diff_pixels_gt16 = 0.4588%`
  - `mean alpha diff ~= 2.0283`
- 增强 diff 图:
  - `Assets/Screenshots/ply_edge_normexp_retry_diff_boost.png`

### 观察

- 这次 wide shot 下,`normExp` 没有直接变成 blank
- 但它的影响范围明显比 `fadeStart=0.60` 更广
- 从增强 diff 图看:
  - 变化不只集中在边缘弧带
  - 中部/大面积 footprint 也一起被改写了

### 当前结论

- `normExp` 这次不能简单描述成“完全不可用”
- 但它依然更像“全局改核”
  - 而不是只处理外缘边界感
- 相比之下:
  - `fadeStart=0.60` 的变化更集中在边缘带
  - 风险更可控
- 因此当前更合理的工程口径是:
  - `normExp` 继续保留为对照参考
  - 但默认工作版本更适合回到 `fadeStart=0.60` 这类 edge-only 方案

## 2026-03-12 16:24:30 +0800 当前对“为什么 `normExp` 以前像没显示”的解释口径

### 已观察到的事实

- 当前 wide shot(`Main Camera`)下:
  - 用户主观上觉得 `normExp` 更好
  - 动态截图也证明它并没有直接 blank
- 之前 close-up 相关实验里:
  - `normExp` 曾出现接近 blank / coverage 明显塌缩
- 当前近景临时拍摄下:
  - `normExp` 没有完全消失
  - 但 screen-space footprint 极大,画面明显对视角非常敏感

### 当前主假设

- `normExp` 不是“全局不能显示”
- 更可能是“强视角 / 强尺寸依赖”:
  - 远景/多层重叠时,它看起来更干净
  - 近景/巨大 splat/局部稀疏时,中外圈 alpha 被压得过低,表面填充会明显下降
- 数值支持:
  - 相比旧核,`normExp` 在
    - `A=0.5` 已下降约 12%
    - `A=0.8` 已下降约 44%
    - `A=0.9` 已下降约 66%
  - 当 splat 在屏幕上很大时,大量像素都落在这段区间里,所以 close-up 下损失会被放大

### 最强备选解释

- 之前那次“像没显示”的结果中,仍可能叠加了当时截图链路 / camera route 的噪声
- 但这条备选解释已经不足以单独解释当前看到的强视角依赖

### 当前结论

- 更合理的当前说法不是:
  - “之前 `normExp` 不能显示出来”
- 而是:
  - “之前在某些 close-up / 巨大 footprint 条件下,`normExp` 的 coverage 会塌得太厉害,看起来像没显示”

## 2026-03-12 17:15:30 +0800 静态证据: 当前场景存在高优先级 HDRP 固定曝光 Volume

### 已观察到的事实

- 当前场景是 HDRP:
  - `pipeline = High Definition (HDRP)`
- `Main Camera` 上存在 `HDAdditionalCameraData`
- 当前有两个 global volume:
  1. `Sky and Fog Volume` priority = 1
  2. `HDRP Toon Showcase Volume` priority = 120
- 高优先级 volume 当前启用:
  - `Exposure active = true`
  - `Bloom active = true`
- 直接读取 `Assets/screen/hdrp_toon/Profiles/HdrpToonShowcaseVolume.asset` 可见:
  - `Exposure.mode = 0`
  - `fixedExposure = 8.791519`
  - `compensation = 0`

### 当前意义

- 这说明 GameView 上确实存在“非 gsplat 自己”的全局亮度控制器
- 而且它优先级高于基础 Sky/Fog volume
- 因此“GameView 偏暗”很可能首先是 HDRP 曝光结果,而不是 Gsplat shader 单独造成
- 但还缺动态证据,需要临时改 exposure 做最小验证

## 2026-03-12 17:19:10 +0800 动态验证: 临时降低 HDRP 固定曝光后,GameView 立刻明显变亮

### 实验目标

- 验证“GameView 偏暗”主要是不是 HDRP 固定曝光导致

### 实验方法

- 目标 volume:
  - `HDRP Toon Showcase Volume`
  - instance id = `-73848`
- 原始值:
  - `Exposure.mode = 0(Fixed)`
  - `fixedExposure = 8.791519`
- 临时实验:
  - 把 `fixedExposure` 降到 `6.0`
  - 其它参数不动
  - 拍 `Main Camera` 同机位图:
    - `Assets/Screenshots/hdrp_exposure_6_maincam_probe.png`
- 实验后已恢复:
  - `fixedExposure = 8.791519`

### 动态结果

- 画面立刻明显变亮
- 说明整体偏暗不是“只要关掉 GammaToLinear 就好”的问题
- 也不是本轮 gsplat alpha 核改动才能解释的量级

### 当前结论

- 这条问题现在已经有静态 + 动态两类证据支持:
  - 静态证据: 存在高优先级 HDRP global volume,启用了 Fixed Exposure,值为 `8.791519`
  - 动态证据: 仅把 `fixedExposure` 从 `8.791519` 降到 `6.0`,GameView 立刻明显变亮
- 因此“整体看起来应该很亮,但现在没达到很亮”的主因,当前更像是 HDRP 曝光设置
- Gsplat shader 仍可能影响局部边缘/coverage 观感
  - 但它不是当前这条“整体偏暗”现象的首要原因

## 2026-03-12 17:28:10 +0800 用户确认亮度主因是 tonemapping,当前已恢复 `normExp`

### 已执行

- 将 `Runtime/Shaders/Gsplat.shader` 恢复到 `normExp` 版本
- 保留 Metal 可编译的字面量常数写法:
  - `kGaussianExp4 = 0.0183156389`
  - `kGaussianInvExp4 = 1.0186573604`
- 重新 compile 验证

### 验证结果

- 编译完成
- Console 无新的 shader error
- 仅有一条无关的 MCP WebSocket warning

### 当前状态

- 当前工作版本重新回到 `normExp`
- 亮度问题当前按用户结论,归因到 tonemapping,不继续算到 shader 头上

## 2026-03-12 16:18:00 +0800 来源: `Runtime/Shaders/Gsplat.shader` vs `supersplat/src/shaders/splat-shader.ts` / PlayCanvas engine 逐文件对照

### 现象

- 用户当前问题已经收敛为:
  - 在恢复 `normExp` 基线后
  - 当前仓库和 `supersplat` shader 还剩哪些关键差异
- 这次不再讨论 HDRP tonemapping.
- 这次只看源码级差异,再补一个最小数值验证,判断哪些差异会真实影响边缘/coverage.

### 已观察到的事实

#### 1. Gaussian 主核公式现在已经对齐

- 当前仓库:
  - `Runtime/Shaders/Gsplat.shader:916`
  - `EvalNormalizedGaussian(a) = max((exp(-a * 4.0) - EXP4) * INV_EXP4, 0.0)`
- `supersplat`:
  - `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:160`
  - `normExp(x) = (exp(x * -4.0) - EXP4) * INV_EXP4`
- 结论:
  - 如果只看 fragment 的 Gaussian 主核,当前已经和 supersplat 同口径.

#### 2. 但 supersplat 的 forward path 没有 `alpha < 1/255` discard

- 当前仓库:
  - `Runtime/Shaders/Gsplat.shader:956`
  - `if (alpha < 1.0 / 255.0) discard;`
- `supersplat` forward:
  - `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:188`
  - 正常渲染分支只算 `alpha = norm * color.a`,然后直接输出
  - 没有 `alpha < 1/255` 的 discard
- `supersplat` 里这个阈值只出现在 pick depth 分支:
  - `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:174`
- 最小数值验证:
  - `normExp(A) < 1/255` 从 `A ~= 0.952306` 开始成立
- 直接含义:
  - 当前仓库会把 `A >= 0.9523` 的那一圈尾部直接裁掉
  - supersplat forward 会保留这段很薄但连续的尾部能量

#### 3. 当前仓库 vertex 仍然执行 `ClipCorner`, supersplat 自定义 shader 没执行

- 当前仓库:
  - `Runtime/Shaders/Gsplat.shader:861`
  - `ClipCorner(gaussCorner, max(baseAlpha, 1.0 / 255.0));`
- `ClipCorner` 数学定义:
  - `Runtime/Shaders/Gsplat.hlsl:153`
  - 按 `alpha` 缩小 corner 的 `offset/uv`
- `supersplat` 自定义 vertex:
  - `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:79`
  - `initCorner(...)` 之后直接 `gl_Position = center.proj + vec4(corner.offset, 0.0)`
  - 这份 shader 里没有调用 `clipCorner(...)`
- 注意:
  - PlayCanvas engine 默认 gsplat vertex 是会 `clipCorner` 的:
    - `/tmp/gsplat_compare/engine/src/scene/shader-lib/glsl/chunks/gsplat/vert/gsplat.js:84`
  - 也就是说:
    - 当前仓库在这一点上更接近 engine 默认实现
    - 但和 supersplat 自己那份展示 shader 仍然不同
- 最小数值验证:
  - `clipCorner(alpha=0.1) = 0.899816`
  - `clipCorner(alpha=0.05) = 0.797736`
  - `clipCorner(alpha=0.01) = 0.483760`
- 直接含义:
  - 低 alpha splat 在当前仓库里会被几何缩小
  - supersplat 自定义 shader 则保留原 quad footprint,只交给 fragment alpha 去衰减

#### 4. 当前仓库还有 `.splat4d` / 动画 / 点模式扩展链路

- 当前仓库额外有:
  - `_Has4D` / `_TimeNormalized` / `_TemporalCutoff`
  - `_VisibilityMode` 全套 reveal/burn/warp 逻辑
  - `_RenderStyleBlend` / `_ParticleDotRadiusPixels` 的 Gaussian <-> ParticleDots morph
- 关键位置:
  - `Runtime/Shaders/Gsplat.shader:271`
  - `Runtime/Shaders/Gsplat.shader:316`
  - `Runtime/Shaders/Gsplat.shader:776`
- `supersplat` 当前自定义 shader 没有这些时域与风格 morph 扩展.
- 直接含义:
  - 就算 fragment 主核对齐了
  - 当前仓库的有效 alpha / 有效 footprint 仍会被额外状态链路改写

#### 5. 颜色链路也不完全一样

- `supersplat`:
  - `/tmp/gsplat_compare/supersplat/src/shaders/splat-shader.ts:132`
  - 先 `color.a = clamp(color.a, 0.0, 1.0)`
  - 再 `prepareOutputFromGamma(max(color.xyz, 0.0))`
- 当前仓库:
  - `Runtime/Shaders/Gsplat.shader:832`
  - 直接读 `_ColorBuffer`
  - `baseAlpha` 没有看到同口径的 `clamp(0,1)`
  - fragment 末端通过 `_GammaToLinear` 开关决定是否 `GammaToLinearSpace`
- 这条差异会影响颜色/亮度口径
- 但它更像颜色管理差异,不是这次“边缘截断感”的第一嫌疑.

#### 6. 当前仓库没有走 PlayCanvas engine 默认 gsplat 的 AA / dither 输出链

- PlayCanvas engine 默认 gsplat:
  - `vert/gsplat.js:57` 会在 `GSPLAT_AA` 下 `clr.a *= corner.aaFactor`
  - `frag/gsplat.js:76` 支持 `opacityDither`
- 当前仓库:
  - `Runtime/Shaders/Gsplat.hlsl` 虽然保留了 `aaFactor` 结构与计算
  - 但 `Runtime/Shaders/Gsplat.shader` 当前主路径没有把 `corner.aaFactor` 乘回 alpha
  - 也没有等价的 dither 路径
- 这意味着当前仓库在“抗锯齿/次像素透明处理”上,也没有完全跟上 engine 默认实现.

### 当前收敛判断

- 如果只问“现在和 supersplat 的 Gaussian 核公式还差吗”:
  - 主核已经基本对齐
- 如果问“现在整体显示链路和 supersplat 还差吗”:
  - 还差,而且最关键的两条仍会真实影响边缘/coverage:
    1. 当前仓库 forward path 仍有 `alpha < 1/255` discard
    2. 当前仓库 vertex 仍有 `ClipCorner`, supersplat 自定义 shader 没有
- 这两条叠在一起,会让当前实现比 supersplat 更早截断尾部,更容易出现“边缘被收紧”的观感.
