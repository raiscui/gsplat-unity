# LATER_PLANS: `.splat4d -> GsplatRenderer` 高斯边缘不透明感排查

## [2026-03-12 10:42:00 +0800] 后续可执行项: 把当前 Gaussian 核对齐到 `supersplat` 的 `normExp`

- 建议最小改动:
  - 在 `Runtime/Shaders/Gsplat.shader` 新增 `EXP4/INV_EXP4/normExp` 逻辑
  - 将 `alphaGauss = exp(-A_gauss * 4.0) * i.color.a` 改为 `alphaGauss = normExp(A_gauss) * i.color.a`
- 验证建议:
  - 用同一 `.splat4d` 场景做 before/after 截图
  - 重点看高 opacity splat 的边缘是否不再出现亮边/圆盘轮廓感
  - 如果需要更稳,可再补一个小型数值回归测试,锁定 `A=1 -> alpha=0`

## [2026-03-12 11:42:00 +0800] 后续关注: package 仍隐含依赖 `com.unity.modules.imageconversion`

- 临时最小 Unity 工程只引用:
  - `com.unity.test-framework`
  - `wu.yize.gsplat`
- 编译时暴露错误:
  - `Runtime/GsplatSog4DRuntimeBundle.cs(1448,22): ImageConversion not found`
- 这说明 package 若单独作为本地依赖接入,当前 `package.json` 还不够自描述
- 后续建议:
  - 审计 `Runtime/GsplatSog4DRuntimeBundle.cs` 使用的 Unity 模块
  - 视 Unity UPM 规则补齐必要 module dependency,或做条件编译隔离

## [2026-03-12 12:26:34 +0800] 状态更新: `com.unity.modules.imageconversion` 依赖缺口已落地修复

- 已完成:
  - `package.json` 已补 `com.unity.modules.imageconversion: 1.0.0`
- 因此 2026-03-12 11:42:00 记录的那条“package 仍隐含依赖 imageconversion”已不再是待办
- 如果后续还要继续审计,更值得查的是:
  - 是否还存在其它被主工程环境掩盖的 built-in module 依赖

## [2026-03-12 15:06:00 +0800] 后续可执行项: 搭一个单 splat / 近景可视化验证场景

- 当前主相机与 Orbit 截图都更适合验证“有没有整体消失”
- 但不够适合定量观察单个 Gaussian 的边缘环是否真的更柔和
- 如果要继续推进这条问题,下一步最值得做的是:
  - 搭一个单 splat 或极小 splat cluster 的近景测试场景
  - 固定相机、关闭会导致帧间抖动的环境因素
  - 对 old/new kernel 做稳定截图或像素剖面比对

## [2026-03-12 14:31:50 +0800] 后续可执行项: 研究 footprint / coverage 校准,不要再直接整条替换 fragment 主核

- 当前强证据已经说明:
  - 对 `s1` 这类高 alpha / window 资产,完整 `normExp` 仍会触发真实 coverage 回退
- 所以下一步更值得做的方向是:
  - 做一个“单 splat / 小 cluster / 稀疏面片”覆盖率测试场景
  - 对比 old / conservative / normExp 在相同 covariance 下的面填充稳定性
  - 排查是否需要在当前 renderer 中补一个 footprint 宽度校准,而不是只改 fragment alpha
- 同时还有一个验证链路问题待单独拆分:
  - 本轮实验回滚后,同一 screenshot route 又出现 blank
  - 这条噪声会继续污染 A/B 结论,需要单独把截图链路稳定下来
