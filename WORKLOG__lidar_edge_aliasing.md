# WORKLOG: LiDAR 高密度扫描边缘锯齿成因分析

## [2026-03-16 22:03:10] [Session ID: 99992] 任务名称: LiDAR 高密度扫描边缘锯齿成因分析

### 任务内容
- 追踪 `LidarAzimuthBins` / `LidarBeamCount` 对 RadarScan 外观的真实影响
- 区分"设计上的离散采样结果"与"精度优化副作用"的边界
- 给出可量化解释,避免只凭肉眼猜测

### 完成过程
- 阅读了 `README.md`、`GsplatRenderer.cs`、`GsplatLidarScan.cs`、`Gsplat.compute`、`GsplatLidarPassCore.hlsl`
- 确认 LiDAR 模式采用的是规则 `beam x azimuthBins` 网格,并非连续 mesh 重建
- 确认每个 splat / external hit 最终都会落到单个 cell,再按 cell-center 射线重建与绘制
- 额外核对了运行时参数防御,发现:
  - `LidarAzimuthBins` 没有上限 clamp
  - `LidarBeamCount` 会被 clamp 到 `512`
  - `CameraFrustum` 模式下 active bins / beams 会按视场比例缩放
- 用脚本把角步长换算成世界空间间距,量化了不同距离下为何仍会出现台阶轮廓

### 总结感悟
- 这类视觉现象最容易被误判成"精度不够"或"优化过头",但真正的第一原则是先搞清楚它渲染的是连续面,还是规则离散点云
- 这条 LiDAR 路线的核心目标是整齐扫描线与 first return 语义,因此边缘台阶感是设计代价的一部分,不是单一 bug

## [2026-03-16 22:16:05] [Session ID: 99992] 任务名称: LiDAR active count 缩放与 beam clamp 跟进分析

### 任务内容
- 响应用户选择的第 2 条跟进方向
- 进一步核对 `beam=512` 上限与 `CameraFrustum` active 分辨率缩放规则

### 完成过程
- 读取了 `ScaleCountKeepingDensity(...)` 的实际实现
- 对照 `GsplatLidarScanTests` 中的期望公式,确认 frustum active count 的正式定义
- 额外发现一个容易误解的细节:
  - `512` clamp 只约束基础 `LidarBeamCount`
  - `CameraFrustum` 下最终 `activeBeamCount` 仍可能因为相机垂直 FOV 更大而高于基础值
- 用一个 `16:9 + 60° vertical FOV` 的例子做了具体换算,证明:
  - `8192` 基础 azimuth 在 frustum 下可能只剩约 `2082` active bins

### 总结感悟
- 真正决定 frustum 轮廓精度的不是 Inspector 上的“名义分辨率”,而是 active count
- 如果用户主观感到“明明调很高还是不够细”,首先该怀疑 activeAzimuthBins 是否被 frustum 口径缩掉了

## [2026-03-16 22:20:26] [Session ID: 99992] 任务名称: LiDAR “大块同深度感” 口径修正

### 任务内容
- 根据用户的进一步澄清,回滚“单点方片是主因”的过强表述
- 将主解释调整为“cell 级 first-return 深度折叠”

### 完成过程
- 结合用户截图与新描述,确认真正需要解释的是“宏观深度块感”
- 将原因重新定位为:
  - 每个 cell 只保留一个深度样本
  - 相邻 cell 在平面区域的深度又往往接近
  - 最终视觉上形成大块片层和乐高式边缘

### 总结感悟
- 当用户不断纠正现象描述时,最重要的不是坚持原分析,而是主动回滚过度拟合的解释
- 这次现象里,“方点图元”更像放大器,而“cell 级深度折叠”才更像主因

## [2026-03-16 22:51:03] [Session ID: 99992] 任务名称: external frustum capture 分辨率最佳方案落地

### 任务内容
- 为 `CameraFrustum + external mesh GPU capture` 提供可控的离屏 capture 分辨率
- 让用户能直接调高 external depth 采样精度,缓解轮廓块状台阶感
- 保持默认行为兼容,避免旧场景无意变慢或变样

### 完成过程
- 在 `GsplatUtils.cs` 中新增 `GsplatLidarExternalCaptureResolutionMode`
- 在 `GsplatRenderer.cs` / `GsplatSequenceRenderer.cs` 中新增序列化字段,并补齐 sanitize / clamp
- 在 `GsplatLidarExternalGpuCapture.cs` 中接入 `Auto / Scale / Explicit` capture 尺寸解析,并增加硬件上限 clamp
- 在两个 Inspector 中暴露新参数,并根据 mode 控制字段可编辑状态
- 在 `GsplatLidarExternalGpuCaptureTests.cs` 中新增 capture size 解析测试
- 在 `README.md` / `CHANGELOG.md` 中同步用户可见说明
- 先用 `dotnet build` 做编译验证,再通过临时克隆工程跑 Unity EditMode tests,绕过主工程被另一个 Unity 实例占用的问题

### 总结感悟
- 对这类"精度可控"需求,最稳的做法不是塞一个魔法倍率,而是把默认来源、相对放大、显式指定三种语义分开
- Unity CLI 的 `-runTests` 在某些版本下确实会出现"加了 `-quit` 反而没生成 XML"的现象,这次再次验证了项目文档里的经验是有效的

## [2026-03-16 22:53:03] [Session ID: 99992] 任务名称: 测试工程 warning 清理收尾

### 任务内容
- 清理本轮验证过程中暴露的 3 个旧编译 warning

### 完成过程
- 把 `GsplatLidarScanTests.cs` 中对 obsolete 属性的 `nameof(...)` 反射改成字符串属性名
- 给 `GsplatSog4DImporterTests.cs` 的 `frameTimesNormalized` 增加默认值,消除未赋值 warning
- 重新执行 `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`,确认 0 warning / 0 error

### 总结感悟
- 顺手收掉验证阶段暴露的旧 warning,能让本轮交付结果更干净,也能减少以后判断"这次是不是又引入了新 warning"时的噪音
