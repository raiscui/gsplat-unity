## Context

当前仓库已经有一条完整的 `.splat4d -> GsplatRenderer` 主链路:

- `Editor/GsplatSplat4DImporter.cs`
  - 负责把 `.splat4d` 导入为现有 `GsplatAsset`
  - 并生成可直接挂 `GsplatRenderer` 的 prefab
- `Runtime/GsplatRenderer.cs`
  - 已经支持 `Velocities / Times / Durations`
  - `TimeNormalized / AutoPlay / Loop` 已经是现有播放控制接口
  - `Has4D` 由这 3 组数组是否存在决定

这说明:
- “`.splat4d -> GsplatRenderer` 能不能工作”不是从零开始的新能力
- 本次 change 更准确的目标是:
  - 补齐工具侧“普通单帧 `.ply -> .splat4d`”正式入口
  - 把“单帧 `.splat4d` 在 Unity 中正常工作”从现有能力提升为正式契约

当前真正的缺口在工具入口与产品口径:

- `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 脚本名和 CLI 仍然偏向“PLY 序列”
  - 当前只接受 `--input-dir`
- `README.md`
  - 已经出现 `--input-ply` 示例
  - 这说明文档与真实 CLI 已经漂移

同时,现有脚本已经有可复用的单帧静态语义基础:

- `average` 模式在 `len(ply_files) == 1` 时会自然写出:
  - `velocity = 0`
  - `time0 = 0`
  - `duration = 1`
- 这组数据刚好符合“静态单帧 `.splat4d`”的目标语义:
  - 整个 `[0,1]` 时间范围内始终可见
  - 位置不会随时间漂移
  - 不需要伪造第二帧

因此,这次设计重点不是发明新格式。
而是把已经存在但没有被正式承诺的单帧能力,整理成一条明确、低歧义、可验证的正式工作流。

## Goals / Non-Goals

**Goals:**

- 为普通单帧 3DGS `.ply` 提供正式的 `.splat4d` 工具入口。
- 继续复用现有 `.splat4d` 64B record 格式,不分叉新的静态格式。
- 明确静态单帧 `.splat4d` 的默认 4D 字段语义:
  - `velocity = 0`
  - `time = 0`
  - `duration = 1`
- 确保单帧 `.splat4d` 导入后继续走现有 `GsplatAsset -> GsplatRenderer` 路线。
- 明确单帧 `.splat4d` 在 `TimeNormalized / AutoPlay / Loop` 下应稳定退化为固定画面。
- 消除 README 与真实 CLI 的漂移,并补齐回归验证。

**Non-Goals:**

- 不新增单帧专用 `.splat4d` 变体格式。
- 不把单帧 `.splat4d` 改走 `.ply` importer 或其它静态资产类型。
- 不改 `.splat4d` 的 record layout,不引入新 header 或 section 结构。
- 不在本 change 内新增高阶 SH 导出能力。
- 不借这次任务重写 `GsplatRenderer` 的 4D 播放架构。

## Decisions

### 1. 单帧支持继续复用现有 `.splat4d -> GsplatAsset -> GsplatRenderer` 主链路

**决定**

- 单帧普通 `.ply` 生成的仍然是标准 `.splat4d`。
- Unity 导入后仍然是现有 `GsplatAsset`。
- 运行时继续由 `GsplatRenderer` 负责显示与播放控制。

**理由**

- 现有 importer 和 renderer 已经支持 `.splat4d` 资产。
- 如果这次把单帧分流到另一套静态路线,会制造新的资产语义分叉。
- 对用户来说,格式一致比“单帧特殊化”更容易理解,也更容易维护文档和测试。

**备选方案**

- 方案A: 单帧 `.splat4d` 改导入为另一种静态资产
  - 放弃。因为这会让同一种格式在单帧和多帧时得到不同 Unity 资产类型。
- 方案B: 直接建议用户继续用 `.ply`
  - 放弃。因为这次目标正是把单帧 `.ply -> .splat4d` 补成正式工具路径。

### 2. 工具侧扩展现有脚本,新增 `--input-ply`,而不是另写一个单帧专用脚本

**决定**

- 继续使用 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 作为唯一主脚本。
- 新增 `--input-ply`。
- `--input-ply` 与 `--input-dir` 互斥。
- 内部统一归一成同一份 `ply_files` 列表后,继续复用既有导出流程。

**理由**

- 当前脚本已经具备完整的 record 构建与写出逻辑。
- 单帧支持的缺口主要是“输入入口没有正式暴露”,不是“编码逻辑完全不存在”。
- 继续扩展现有脚本,比额外增加 `ply_to_splat4d.py` 更符合“改良胜过新增”。

**备选方案**

- 方案A: 新增一个单帧包装脚本
  - 放弃。因为它只会重复参数解析、错误处理和 README 维护。
- 方案B: 不新增参数,只要求用户把单个 `.ply` 放到目录里
  - 不选。因为这会继续保留产品入口歧义。

### 3. 单帧 `.ply` 的导出语义直接复用现有 `average` 路径,不发明新的 `static` 模式

**决定**

- 当输入只有 1 个 `.ply` 时:
  - `average` 模式直接生成单帧静态 `.splat4d`
  - 默认写入:
    - `velocity = 0`
    - `time = 0`
    - `duration = 1`
- `keyframe` 模式对单帧输入保持非法,并输出明确错误。

**理由**

- 现有实现已经天然具备这组静态默认值。
- 这组值与现有 4D 播放语义兼容:
  - 不运动
  - 全时可见
  - 没有时间窗空洞
- 新增 `static` 模式只会在语义上重复 `average + 1 frame`。

**备选方案**

- 方案A: 新增 `static` 模式
  - 放弃。因为语义重复,会增加文档和测试面。
- 方案B: 单帧输入时偷偷把 `keyframe` 自动转成 `average`
  - 不选。因为这会让 CLI 行为不够明确,更难排查用户误用。

### 4. Unity 侧不新增单帧专用 renderer 分支,而是用契约和测试硬化现有行为

**决定**

- 单帧 `.splat4d` 导入后仍然保留 4D 数组。
- `GsplatRenderer` 继续走现有 `Has4D` 路线。
- 本次优先补:
  - 单帧导入回归测试
  - 播放控制稳定性验证
  - 必要的最小 guard / 断言

**理由**

- 现有静态默认字段已经足以表达“固定画面”。
- 在 `velocity=0,time=0,duration=1` 下,播放控制即使存在,视觉上也应稳定不变。
- 当前没有静态证据表明必须新开一条“单帧 special-case renderer”路径才能正确工作。

**备选方案**

- 方案A: 单帧时把 4D 数组剥掉,强行伪装成 3D-only 资产
  - 放弃。因为这会让 `.splat4d` 的语义依赖导出来源,不够直观。
- 方案B: 新增显式 `IsStaticSplat4D` 分支
  - 暂不选。当前更需要的是先把现有链路验证和硬化。

### 5. 这次 change 的重点是把“文档承诺、工具入口、Unity 验证”对齐

**决定**

- README 中关于 `--input-ply` 的文案必须与真实 CLI 保持一致。
- 自动化验证至少覆盖:
  - 工具侧单帧输入
  - importer 侧单帧 `.splat4d`
  - runtime / playback 侧的稳定退化语义
- 除最小夹具外,应尽量再补一条真实普通 3DGS `.ply` 的验收证据。

**理由**

- 当前最明显的现象就是 README 和 CLI 漂移。
- 如果只有代码没有验证,单帧支持很容易再次退回成“看起来像支持”。
- 如果只有最小测试夹具,无法证明真实普通 3DGS 输入真的能闭环。

**备选方案**

- 方案A: 只补 CLI 参数,不补回归测试
  - 放弃。因为这不能形成“正式支持”的证据闭环。

## Risks / Trade-offs

- [风险] `README` 先于 CLI 暴露了 `--input-ply`,说明文档和实现已经漂移
  - 缓解: 本次必须同时更新 CLI、README 和测试,避免再次分叉

- [风险] 单帧 `.splat4d` 虽然逻辑上是静态的,但当前仍会走 `Has4D` 路线
  - 缓解: 先接受这条稳定主路径,本次优先保证正确性; 性能优化留到后续独立任务

- [风险] 用户可能误把“单帧 `.ply` 支持”理解成“支持任意普通点云 PLY”
  - 缓解: 在文档和错误信息中明确输入仍然要求 Gaussian Splatting 风格字段

- [风险] 单帧输入下 `keyframe` 语义容易让用户误会
  - 缓解: 保持显式报错,不要做隐式模式切换

- [风险] 若只用合成最小样例验证,真实普通 3DGS 资产仍可能暴露字段口径问题
  - 缓解: 除最小测试外,补一条真实样例转换与 Unity 验证

## Migration Plan

- 对现有 `.splat4d` 资产:
  - 不需要迁移
  - 不需要改 importer version
  - 不需要重写格式
- 对现有多帧工具调用:
  - `--input-dir` 保持兼容
- 对新的单帧工具调用:
  - 增加 `--input-ply`
  - 单帧普通 `.ply` 走 `average` 语义即可
- 回滚策略:
  - 如果后续发现 `--input-ply` 设计有问题,可以回滚参数入口
  - 但单帧 `.splat4d` 的 Unity 契约、文档修正和测试资产不应一起回滚

## Open Questions

- 是否要把“目录里只有 1 个 `.ply`”继续作为等价支持路径写进文档,还是主推 `--input-ply`?
- 本次要锁定哪份真实普通 3DGS `.ply` 作为最终验收样例?
- 单帧 `.splat4d` 未来是否值得做 3D-only 优化分支,以减少不必要的 4D buffer 成本?
