## Why

当前仓库已经支持 `.sog4d` 序列 bundle,但产品入口、离线工具文案和 Unity 侧使用语义仍然明显偏向 `time_*.ply` 多帧序列。对于只有一帧的高斯资产,用户现在无法得到一条被正式承诺的 "`single .ply -> .sog4d -> Unity 正常显示与使用`" 工作流,这会让分发格式选择、导入预期和运行时行为都处于模糊状态。

## What Changes

- 新增一个正式的 `.ply -> .sog4d` 转换能力,覆盖单帧 `.ply` 与多帧 `.ply` 序列两类输入。
- 为单帧输入定义明确的 `.sog4d` 打包口径:
  - 如何组织输入
  - 默认时间映射与元数据语义
  - 转换期校验与错误信息
- 明确 Unity 导入器对 `frameCount=1` 的要求:
  - 必须成功导入
  - 必须生成可被现有组件直接引用的资产
  - 不得因为缺少“下一帧”而失败或生成半成品
- 明确单帧 `.sog4d` 在运行时/播放 API 下的退化语义:
  - `TimeNormalized` 仍可设置
  - 但显示结果应稳定等价于固定帧
  - 不应读取不存在的第二帧数据
- 补充 README / Tools 文档 / 测试,把单帧转换和 Unity 使用链路写成正式支持路径。

## Capabilities

### New Capabilities

- `sog4d-ply-conversion`: 定义从 `.ply` 输入打包生成 `.sog4d` 的离线转换契约,覆盖单帧文件与多帧序列的输入发现、元数据默认值、自检与失败语义。

### Modified Capabilities

- `sog4d-unity-importer`: 扩展 importer 要求,明确 `frameCount=1` 的 `.sog4d` bundle 必须被正常导入、引用和显示,不能因序列插值假设而失败。
- `4dgs-keyframe-motion`: 扩展单帧 keyframe 资产在 `TimeNormalized` 下的评估语义,明确单帧情况下索引退化为固定帧,且系统不得依赖不存在的相邻帧。

## Impact

- Tools:
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - `Tools~/Sog4D/README.md`
  - 可能新增更直观的单帧包装入口,或将现有脚本文案/CLI 扩展为正式支持单帧
- Editor:
  - `Editor/GsplatSog4DImporter.cs`
  - 相关 importer 校验与错误提示
- Runtime:
  - `Runtime/GsplatSog4DRuntimeBundle.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - 任何会默认读取双帧窗口的单帧退化路径
- Docs / Tests:
  - `README.md`
  - `Documentation~/`
  - `Tests/Editor/`
  - 需要补齐单帧 `.ply -> .sog4d -> Unity` 的回归验证
