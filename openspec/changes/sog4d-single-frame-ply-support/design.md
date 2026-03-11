## Context

当前仓库已经具备完整的 `.sog4d` 主链路:

- `Tools~/Sog4D/ply_sequence_to_sog4d.py` 负责离线打包
- `Editor/GsplatSog4DImporter.cs` 负责导入 ZIP bundle 与 `meta.json`
- `Runtime/GsplatSequenceAsset.cs` / `Runtime/GsplatSequenceRenderer.cs` 负责时间评估与运行时播放

但这条链路的“产品入口”仍然明显偏向多帧序列:

- CLI 只有 `--input-dir`,帮助文案明确写的是“包含 `time_*.ply` 的目录”
- README 示例只展示了 `time_*.ply` 多帧打包
- OpenSpec 当前还没有一份专门描述 “`.ply -> .sog4d` 转换契约” 的 capability

同时,现有代码已经提供了一部分单帧友好基础:

- `sog4d-sequence-encoding` 已定义 `frameCount == 1` 时 `uniform timeMapping` 的唯一帧时间为 `t_0 = 0.0`
- `Runtime/GsplatSequenceAsset.cs` 的 `EvaluateFromTimeNormalized(...)` 已在 `frameCount == 1` 时返回 `i0 = 0, i1 = 0, a = 0`
- `Editor/GsplatSog4DImporter.cs` 与 `Runtime/GsplatSog4DRuntimeBundle.cs` 目前只要求 `frameCount > 0`,没有静态证据表明它们硬性要求至少两帧

因此,这次 change 的本质不是“新增另一种格式”,而是把“单帧 `.ply` 作为 `.sog4d` 的正式输入形态”补成一条清晰、可验证、可文档化的主路径。

## Goals / Non-Goals

**Goals:**

- 在不分叉现有 `.sog4d` 架构的前提下,正式支持单帧 `.ply -> .sog4d` 转换。
- 让离线工具对“单帧输入”有明确、低歧义的入口与错误语义。
- 明确 Unity 导入器与运行时对 `frameCount = 1` 的行为承诺:
  - 成功导入
  - 可被现有 `GsplatSequenceRenderer` 正常引用
  - 不依赖不存在的第二帧
- 为单帧路径补齐 README / Tools 文档 / EditMode 回归测试,让能力从“可能能跑”升级为“正式支持”。

**Non-Goals:**

- 不新增第二套单帧专用容器格式。
- 不把单帧 `.sog4d` 回退导入成 `GsplatAsset` / `.ply` importer 路线。
- 不重写现有 `.sog4d` decode / sort / render 主链路。
- 不在本 change 内引入新的压缩算法或改变已有 `.sog4d` bundle schema。

## Decisions

### 1. 单帧支持继续复用现有 `.sog4d` 主链路,不新增平行格式

**决定**

- 单帧 `.ply` 仍然生成标准 `.sog4d` bundle。
- Unity 侧仍然导入为 `GsplatSequenceAsset` + `GsplatSequenceRenderer` 路线。
- 不新增 “single-frame-sog4d” 或 “static-sog4d” 之类的平行概念。

**理由**

- 现有容器与时间语义已经允许 `frameCount = 1`。
- 现有 runtime 评估函数已经有单帧安全分支。
- 如果把单帧资产改走 `GsplatAsset` 或新格式,会造成:
  - 两套导入物语义
  - 两套文档和测试口径
  - Player runtime bundle / chunk streaming 路线无法统一

**备选方案**

- 方案A: 单帧 `.ply` 直接继续导入为 `GsplatAsset`
  - 放弃。因为这会让 `.sog4d` 在“单帧”和“多帧”之间有不同资产类型,破坏格式一致性。
- 方案B: 新增单帧专用 `.sog4d` 变体
  - 放弃。因为当前没有证据显示现有 bundle schema 无法承载单帧。

### 2. 离线工具采用“扩展现有脚本”的方式补单帧入口,而不是新增包装脚本

**决定**

- 继续以 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 为唯一主脚本。
- 在 CLI 层新增单帧友好的显式输入方式,推荐增加 `--input-ply`。
- 保留现有 `--input-dir` 以兼容多帧序列工作流。
- 在内部把两种入口统一归一到同一套 `ply_files` 列表,后续编码路径完全复用。

**理由**

- 当前主脚本已经承担了:
  - PLY 读取
  - meta 生成
  - WebP / centroids / delta-v1 输出
  - validate / normalize-meta
- 新增包装脚本虽然表面更快,但长期会复制:
  - 参数解释
  - 错误处理
  - README 示例
  - 回归测试
- 用一个脚本统一“单帧”和“序列”入口,更符合“改良胜过新增”。

**备选方案**

- 方案A: 新增 `ply_to_sog4d.py`
  - 放弃。因为它几乎只会包一层参数适配,长期价值低于维护成本。
- 方案B: 只保留 `--input-dir`,文档说明“目录里放 1 个 `.ply` 也行”
  - 不选。因为用户目标是“单帧文件转换”,继续只暴露目录语义会让入口仍然不清晰。

### 3. 单帧 `.sog4d` 在 Unity 中仍然是“序列资产”,但运行时语义要显式退化为固定帧

**决定**

- 单帧 bundle 导入后仍生成 `GsplatSequenceAsset`。
- `TimeNormalized`、`AutoPlay`、`Loop` 等 API 继续保留,不做特殊裁剪。
- 运行时规范中明确:
  - `i0 = 0`
  - `i1 = 0`
  - `a = 0`
  - decode / interpolate pass 不得因为“第二帧不存在”而访问无效 layer

**理由**

- 这与现有 `GsplatSequenceAsset.EvaluateFromTimeNormalized(...)` 的实现方向一致。
- 对外 API 保持一致,用户不需要为“单帧序列”写不同的驱动代码。
- 这条路线可以同时覆盖:
  - Editor importer 子资产
  - Player runtime bundle 加载
  - chunk streaming 关闭或自动回退场景

**备选方案**

- 方案A: 单帧时在 importer 阶段转成 `GsplatAsset`
  - 放弃。因为这会让 `.sog4d` 的 runtime load / chunk / decode 路线在单帧场景失去一致性。
- 方案B: 保持当前实现但不写清单帧承诺
  - 不选。因为这正是当前产品口径模糊的根源。

### 4. 这次 change 以“硬化现有实现 + 补合同 + 补证据”为主,不重写 decode 架构

**决定**

- 不修改 `.sog4d` 的 bundle schema。
- 不新增新的 compute shader 路线。
- 优先做以下硬化:
  - 工具输入解析与错误提示
  - importer / runtime 单帧 guard
  - README / Tools 文档
  - 单帧回归测试

**理由**

- 现有代码已经有明显的单帧兼容基础,这意味着真正缺的是“产品承诺”和“回归证据”。
- 在没有发现动态失败证据前,不应该为了“看起来更完整”去改核心架构。

**备选方案**

- 方案A: 借机重构 `GsplatSequenceRenderer` 的 decode 逻辑
  - 放弃。因为这会扩大 change 半径,而且当前没有证据显示核心 decode 是单帧问题根因。

### 5. 测试策略以“最小可证伪实验”为核心,分别覆盖工具、导入器、运行时退化语义

**决定**

- 增加三类回归验证:
  - 工具侧: 单帧输入可以成功打包,并生成 `frameCount = 1` 的合法 bundle
  - importer 侧: 单帧 `.sog4d` 可成功导入,生成可引用的 `GsplatSequenceAsset`
  - runtime 侧: `frameCount = 1` 时 `EvaluateFromTimeNormalized(...)` 与相关渲染准备路径不会访问不存在的第二帧

**理由**

- 只有文档没有测试,很容易再次回退成“理论支持”。
- 只有 importer 测试没有工具测试,不能证明用户真的拿得到正确的单帧 bundle。
- 只有 runtime 测试没有 importer 测试,也不能证明 Unity 主链路真的闭环。

**备选方案**

- 方案A: 只改 README,不补自动化测试
  - 放弃。因为这无法提供“正式支持”的证据。

## Risks / Trade-offs

- [风险] 现有 decode / buffer 绑定代码里可能仍有“默认读取 i1 layer”的隐式路径,文本搜索不一定能一次看全
  - 缓解: 先补最小单帧 importer/runtime 测试,再根据失败点做最小修复

- [风险] 给脚本新增 `--input-ply` 后,命令行参数组合会变复杂,容易与 `--input-dir` 同时出现
  - 缓解: 明确做互斥校验,要求两者二选一,错误信息直接告诉用户正确用法

- [风险] 用户可能把“单帧 `.ply` 支持”理解成“导入后表现成静态 `GsplatAsset`”
  - 缓解: 在 README 和 Inspector 文档中明确说明它仍是 `GsplatSequenceAsset`,只是 `frameCount = 1`

- [风险] 若现有 README 示例只补一条单帧命令,但不解释“为什么仍是 sequence asset”,用户仍可能困惑
  - 缓解: 在文档里同时给出“单帧输入示例”和“Unity 侧使用语义说明”

## Migration Plan

- 对现有 `.sog4d` 资产:
  - 无需迁移
  - 无需改 meta version
  - 无需重打包
- 对现有工具调用:
  - `--input-dir` 保持兼容
  - 新增 `--input-ply` 仅作为更明确的单帧入口
- 回滚策略:
  - 若实现阶段发现 `--input-ply` 带来过多 CLI 复杂性,可以回滚到“保留内部输入归一逻辑 + 仅文档承诺目录单帧支持”
  - 但无论是否保留单独参数,`frameCount = 1` 的 importer/runtime 支持与测试都应保留

## Open Questions

- 是否需要在 Tools README 中把“目录里只有 1 个 `.ply`”也列为等价支持路径,还是只主推 `--input-ply`?
- 是否需要补一个最小单帧 `.sog4d` 示例资产到测试资源,便于 importer 回归而不依赖运行打包脚本?
- 现有 `GsplatSequenceRenderer` 的 decode 提交流程里,是否还有值得显式加断言的“i0 != i1”隐含假设?
