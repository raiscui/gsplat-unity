# WORKLOG: Unity IMGUI `EndLayoutGroup` 布局报错

## 2026-03-11 23:00:00 +0800 任务名称: 修复 vInspector / vHierarchy 包装 Inspector 时的布局泄漏

### 任务内容

- 排查 Unity Editor 中 `EndLayoutGroup: BeginLayoutGroup must be called first` 的来源.
- 在当前工程内找到最小修复点,避免 Inspector 包装器在 `ExitGUI` 提前退出时跳过布局收尾.

### 完成过程

- 先读 `Editor.log`,确认这条报错不是当前 `.sog4d` 导入后第一次出现.
- 进一步确认它会在无关资源导入后也出现,因此不把根因直接归到 `wu.yize.gsplat` package.
- 排查工程级 Editor 扩展后,锁定两个共同模式:
  - `Assets/vInspector/VInspectorComponentWindow.cs`
  - `Assets/vHierarchy/VHierarchyComponentWindow.cs`
- 它们都在:
  - `EditorGUILayout.BeginScrollView(...)`
  - `editor?.OnInspectorGUI()`
  - `EditorGUILayout.EndScrollView()`
  之间缺少 `try/finally`.
- 进行了最小修复:
  - 用 `try/finally` 包住 `editor?.OnInspectorGUI()`
  - 保证 `EndIndent(...)` / `EndScrollView()` / `labelWidth` 复位在提前 `ExitGUI` 时仍然执行

### 验证

- 修复前 `Editor.log` 中该错误累计计数: `9`
- 触发一次 Unity `资产 > 刷新` 与脚本重编译
- 修复后 `Editor.log` 总行数继续增长,但该错误累计计数仍为 `9`,没有新增

### 总结感悟

- 这类 IMGUI 报错很容易被误判成“最近一次导入的资源有问题”.
- 但真正高频的根因,往往是工程级 Inspector 包装器没有在 `ExitGUI` 路径上做布局收尾.
