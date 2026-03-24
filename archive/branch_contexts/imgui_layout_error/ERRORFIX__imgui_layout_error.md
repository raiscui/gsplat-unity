# ERRORFIX: Unity IMGUI `EndLayoutGroup` 布局报错

## 2026-03-11 23:01:00 +0800

### 现象

- Unity Editor Console / `Editor.log` 报:
  - `EndLayoutGroup: BeginLayoutGroup must be called first.`
  - 栈只落到 Unity 内部:
    - `GUILayoutUtility.EndLayoutGroup`
    - `GUIView.EndOffsetArea`
    - `HostView.InvokeOnGUI`

### 错误分析

- 只有 Unity 内部栈,不能直接把锅甩给最近一次改动或最近一次导入的资源.
- 通过历史 `Editor.log` 对比发现:
  - 这条错误在当前 `.sog4d` 导入之前就已经出现过多次.
  - 因此“`.sog4d` importer 本身导致布局错误”这个假设不成立.

### 原因

- 当前更有证据支持的原因是工程里的 Inspector 包装窗口:
  - `Assets/vInspector/VInspectorComponentWindow.cs`
  - `Assets/vHierarchy/VHierarchyComponentWindow.cs`
- 它们把 `editor?.OnInspectorGUI()` 放在 `BeginScrollView/EndScrollView` 中间,但没有 `try/finally`.
- 当子 Inspector 提前 `ExitGUI` 时:
  - `EndScrollView()` 被跳过
  - 宿主窗口下一步收尾就会触发 `EndLayoutGroup`

### 修复

- 在两个包装点都补 `try/finally`.
- 无论子 Inspector 是否提前退出,都强制执行:
  - `EndIndent(...)`
  - `EditorGUILayout.EndScrollView()`
  - `EditorGUIUtility.labelWidth = 0`

### 验证

- 修复前 `Editor.log` 计数: `9`
- 触发 Unity `资产 > 刷新` 和脚本重编译后:
  - `Editor.log` 总行数增加
  - 但 `EndLayoutGroup` 计数仍保持 `9`
- 说明本轮验证路径下没有再新增同类报错
