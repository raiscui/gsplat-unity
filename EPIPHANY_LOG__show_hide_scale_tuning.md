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
