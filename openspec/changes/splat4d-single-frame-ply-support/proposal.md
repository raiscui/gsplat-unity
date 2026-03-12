## Why

当前仓库已经有 `.splat4d -> GsplatRenderer` 的导入和显示路径,但 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 仍然只接受 `--input-dir`,产品入口仍然偏向多帧 PLY 序列。对于只有一帧的普通 3DGS `.ply`,用户还拿不到一条被正式承诺的 "`single .ply -> single-frame .splat4d -> Unity 正常工作`" 工作流,这会让工具入口、文档预期和 Unity 侧验收边界继续处于模糊状态。

这里的职责边界需要明确:
- 普通 3DGS 单帧 `.ply -> .splat4d` 是工具侧职责
- `.splat4d -> GsplatRenderer` 已经有现有链路,本次不是从零发明新运行时组件
- 这次要补的是“单帧 `.splat4d` 在 Unity 中正常工作”的正式保证与验收标准

## What Changes

- 新增一个正式的普通 3DGS `.ply -> .splat4d` 转换能力,重点覆盖“单个 `.ply` 文件生成单帧 `.splat4d`”这条工具入口。
- 为单帧工具入口定义明确的导出语义:
  - 输入发现与参数规则
  - 静态单帧写入 `.splat4d` 时的默认 4D 字段口径
  - 转换期错误信息与失败语义
- 明确单帧 `.splat4d` 仍然是合法的现有 `.splat4d` 资产,而不是新的静态格式分支。
- 扩展 Unity 侧既有 `.splat4d -> GsplatRenderer` 契约,明确由普通单帧 `.ply` 生成的 `.splat4d` 必须:
  - 正常导入为现有资产类型
  - 可直接挂到 `GsplatRenderer`
  - 在 `TimeNormalized / AutoPlay / Loop` 存在时稳定退化为固定姿态,而不是依赖不存在的额外帧
- 补齐 README / 工具说明 / 自动化验证,让“单帧 `.ply -> .splat4d -> Unity`”成为正式支持路径。

## Capabilities

### New Capabilities

- `splat4d-ply-conversion`: 定义普通 3DGS `.ply` 到 `.splat4d` 的离线转换契约,重点覆盖单文件输入、静态默认 4D 字段写法、CLI 规则与失败语义。

### Modified Capabilities

- `4dgs-core`: 扩展 `.splat4d` 导入与静态默认语义,明确由普通单帧 `.ply` 生成的 `.splat4d` 必须作为合法静态资产导入,并可直接被现有 `GsplatRenderer` 使用。
- `4dgs-playback-api`: 扩展播放控制对静态单帧 `.splat4d` 的要求,明确这类资产在 `TimeNormalized / AutoPlay / Loop` 下必须稳定工作,视觉结果退化为固定帧而不是异常的伪动态行为。

## Impact

- Tools:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 相关工具测试与使用文档
- Editor:
  - `Editor/GsplatSplat4DImporter.cs`
  - 与 `.splat4d` 导入验收相关的测试
- Runtime:
  - `Runtime/GsplatRenderer.cs`
  - 与静态单帧 `.splat4d` 播放退化语义相关的逻辑与验证
- Docs / Validation:
  - `README.md`
  - `Documentation~/`
  - `Tests/Editor/`
  - 需要把“普通单帧 `.ply -> .splat4d -> Unity`”纳入正式回归路径
