# EPIPHANY_LOG__show_hide_scale_tuning

## 2026-03-15 23:09:00 +0800 主题: Unity 工程实际加载的不是当前工作目录这份包

### 发现来源
- 在验证阶段读取工程根 `../../Packages/manifest.json` 时发现 `wu.yize.gsplat` 指向外部 file package。
- 随后比对 `realpath`、`stat inode` 与 git 根路径,确认当前目录与 manifest 目标目录不同。

### 核心问题
- 如果继续只修改工程内 `Packages/wu.yize.gsplat`,Unity 运行时与 EditMode 测试都可能不会使用这些改动。

### 为什么重要
- 这会制造“代码已经改了,但运行完全没变化”或“测试结果与当前目录 diff 对不上”的假象。
- 对图形/动画问题尤其危险,因为很容易被误判成 shader 参数或缓存问题。

### 未来风险
- 后续若在错误副本上继续开发,会同时污染两份包代码,还会让验证证据失真。

### 当前结论
- 已确认 Unity 当前实际使用的是 `/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity`。
- 当前目录这份包是另一份独立 git 工作树,不能直接视为运行时生效代码。

### 后续讨论入口
- 后续凡是要验证 shader/C# 生效性,先看工程 `Packages/manifest.json`。
- 对 file package 方案,应优先在实际引用目录上做验证,必要时再同步回仓库副本。

## 2026-03-15 23:14:00 +0800 主题: 上一条外部包判断已被新证据推翻

### 被推翻的旧判断
- 23:09 那条记录里,我一度把 manifest 的 `file:` 路径当成了 Unity 当前实际使用的包来源。

### 推翻证据
- `../../Packages/packages-lock.json` 中 `wu.yize.gsplat` 为 `source: embedded`。
- `../../Gsplat.csproj` 与 `../../Gsplat.Tests.Editor.csproj` 都编译 `Packages/wu.yize.gsplat/...` 当前目录对应源码。

### 修正后的结论
- 当前工程虽然 manifest 里仍保留 `file:` 依赖写法,但实际生效来源是 embedded package。
- 本次 show/hide world-speed 改动所在的当前目录,就是 Unity 实际验证目标。

### 提醒
- 对 Unity package 来源的判断,不能只看 manifest,还要结合 `packages-lock.json` 与生成的 `.csproj` 一起确认。

## [2026-03-18 09:37:07 +0800] [Session ID: unknown] 主题: 共享一套显隐状态机,无法表达重叠式切换

### 发现来源
- 在重新诊断 `show-hide-switch-高斯` 时,用户把语义进一步收紧为:
  - 雷达 hide 要完整跑完
  - Gaussian show 在 hide 过半时启动
  - 中段两者必须同时可见
- 复核 `GsplatRenderer` / `GsplatSequenceRenderer` 后发现,当前 show/hide 体系只有一套共享 `m_visibilityState`

### 核心问题
- 只要一套共享状态既要表示“旧对象正在 hide”,又要表示“新对象正在 show”,两者就一定会互相覆盖。
- 继续堆 bypass,最多只能做出“看起来没那么突兀”的假象,很难真正保证两条轨都完整执行。

### 为什么重要
- 这不是这次 Radar/Gaussian 按钮特有的问题。
- 只要以后再出现:
  - A 退场动画未结束
  - B 入场动画已开始
  - 两者还要求一段时间内共存
- 同样会撞上这类状态表达不足的问题。

### 未来风险
- 若继续沿用单轨补丁思路,后续容易反复出现:
  - 某一条轨被提前中断
  - overlap 阶段某些门禁误关
  - 测试看似通过,但真实播放时仍有“中途突然断电”的体感

### 当前结论
- 对“重叠切换”这类需求,更稳的路径通常是:
  - 保留共享主轨处理最终稳定态
  - 另加一条最小独立轨来承载 overlap 期间仍需继续推进的旧动画
- 这轮里,那条独立轨就是 Radar->Gaussian 专用 LiDAR hide overlay 轨。

### 后续讨论入口
- 下次再做类似“旧视觉退场 + 新视觉进场并行”的功能时,优先先问:
  - 当前状态机能否同时表达两条轨?
  - 是否需要一条 snapshot / overlay / keepalive 的独立轨?

## [2026-03-18 19:35:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 主题: overlap 编排不要直接复用带 cancel 副作用的公开 API

### 发现来源
- 在实现 `show-hide-switch-高斯` 的 dual-track overlap 时,动态测试发现:
  - 明明已经抓到了专用 hide overlay
  - 但 overlap 一启动,现场还是马上掉回共享 show 轨

### 核心问题
- 公开 API 往往自带“清 pending / 清中间态 / 重置快照”的保护逻辑。
- 这些保护在普通单步操作里是优点,但在 overlap 编排里可能正好会把需要保留的中间态抹掉。

### 为什么重要
- 这类 bug 非常隐蔽:
  - 静态看代码像是“已经 capture 了”
  - 现场却还是像“根本没 capture”
- 如果没有动态测试,很容易继续误判成 easing、阈值或 shader 问题。

### 当前结论
- 对“先抓一份中间态,再继续执行下一段动画”的功能:
  - 最稳的做法不是直接套公开 API
  - 而是把“真正执行过渡”的内部 helper 抽出来
  - 让公开 API 继续保留它的 reset/cancel 语义

### 后续讨论入口
- 以后再做类似 overlap / timeline / staged handoff 的功能时,优先检查:
  - 当前调用的公开 API 是否有 `Cancel*` / `Reset*` / `Clear*` 副作用
  - 是否应该拆出一个无副作用的内部过渡 helper
