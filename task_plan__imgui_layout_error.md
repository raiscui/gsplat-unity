# 任务计划: Unity IMGUI `EndLayoutGroup` 布局报错排查

## 目标

定位当前 Unity Editor 中 `EndLayoutGroup: BeginLayoutGroup must be called first` 的真实触发代码路径。
如果问题在本 package,完成最小修复并给出验证证据。

## 阶段

- [ ] 阶段1: 现场与日志确认
- [ ] 阶段2: 候选 Editor UI 路径排查
- [ ] 阶段3: 最小修复与验证
- [ ] 阶段4: 记录结论与交付

## 关键问题

1. 这条报错是否能在 `Editor.log` 中拿到更完整的上下文或附加堆栈?
2. 触发点是在本 package 的自定义 Inspector / EditorWindow,还是 Unity 工程其他脚本?
3. 是否与刚刚导入 `.sog4d` 后打开某个 Inspector 有直接关系?

## 当前主假设与备选解释

- 主假设:
  - 某个自定义 Inspector 的 `EditorGUILayout.Begin... / End...` 配对失衡.
- 备选解释:
  - 不是本 package 代码,而是工程其它 Editor 扩展或 Unity 内置 Inspector 在特定资产状态下触发.

## 做出的决定

- 先找动态证据,再改代码.
  - 理由: 当前用户只提供了 Unity 通用报错栈,还没有指向具体脚本文件.

## 遇到的错误

- (待补)

## 状态

**目前在阶段1**
- 我正在先抓 `Editor.log` 与包内 Editor 脚本的布局调用热点.
- 下一步会优先排查最近可能相关的自定义 Inspector.

## 2026-03-11 22:56:00 +0800 进展: 已锁定最可疑的包装点

### 已验证事实

- `Editor.log` 里的这条 `EndLayoutGroup` 错误不是第一次出现.
- 它早在当前 `.sog4d` 导入之前就已经多次发生,且会出现在无关资产导入之后.
- 当前工程启用了全局 Inspector/Hierarchy 扩展:
  - `Assets/vInspector`
  - `Assets/vHierarchy`
- 两个最可疑包装点:
  - `Assets/vInspector/VInspectorComponentWindow.cs`
  - `Assets/vHierarchy/VHierarchyComponentWindow.cs`
- 这两个文件都存在同一模式:
  - `EditorGUILayout.BeginScrollView(...)`
  - `editor?.OnInspectorGUI()`
  - `EditorGUILayout.EndScrollView()`
  - 但没有 `try/finally` 保证 `EndScrollView()` 必定执行

### 当前主假设

- 当被包装的 Inspector 在重绘期间触发 `ExitGUIException` 或其它提前退出时:
  - `EndScrollView()` 会被跳过
  - 宿主 InspectorWindow 在后续收尾时就会报 `EndLayoutGroup: BeginLayoutGroup must be called first`

### 下一步行动

- [ ] 对这两个包装点做最小修复:
  - 用 `try/finally` 包住 `editor?.OnInspectorGUI()`
  - 确保 `EndIndent` / `EndScrollView` / `labelWidth` 复位一定执行
- [ ] 触发一次 Editor 刷新,观察 `Editor.log` 是否继续新增同类报错

### 状态

**目前在阶段3**
- 证据已经足够支持先做最小修复实验.

## 2026-03-11 23:02:00 +0800 结果: 最小修复实验通过

### 已完成

- [x] 已修复:
  - `Assets/vInspector/VInspectorComponentWindow.cs`
  - `Assets/vHierarchy/VHierarchyComponentWindow.cs`
- [x] 修复方式:
  - 在 `BeginScrollView` 包装的 `editor?.OnInspectorGUI()` 外层补 `try/finally`
  - 保证 `EndIndent` / `EndScrollView` / `labelWidth` 复位一定执行
- [x] 动态验证:
  - 记录修复前 `Editor.log` 错误计数 = `9`
  - 触发一次 Unity `资产 > 刷新`
  - 等待脚本重编译与日志稳定
  - 复查后错误计数仍是 `9`,没有新增

### 当前结论

- 本轮最小验证支持当前主假设:
  - 问题更像工程级 Inspector 包装器的布局收尾缺失
  - 不是 `wu.yize.gsplat` package 本身的 `.sog4d` 导入逻辑造成

### 状态

**目前在阶段4**
- 已完成最小修复与同路径验证.
- 接下来可以整理交付说明,或按需要继续做更强的人手复现验证.
