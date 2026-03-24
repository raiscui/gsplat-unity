# 笔记: Unity IMGUI `EndLayoutGroup` 布局报错

## 2026-03-11 22:49:00 +0800 初始现象

- 用户反馈 Unity Editor Console 报错:
  - `EndLayoutGroup: BeginLayoutGroup must be called first.`
  - `UnityEngine.GUIUtility:ProcessEvent (int,intptr,bool&)`
- 当前还没有拿到额外文件名或更完整堆栈.

## 初步判断

- 这类报错通常是 IMGUI 布局 API 配对失衡:
  - `BeginHorizontal/EndHorizontal`
  - `BeginVertical/EndVertical`
  - `BeginFadeGroup/EndFadeGroup`
  - `BeginScrollView/EndScrollView`
  - 或者 `GUILayout.BeginArea/EndArea`
- 仅凭当前用户贴出的栈,还不能确认一定是本 package 导致.

## 2026-03-11 22:57:00 +0800 证据补充: 当前更像工程级 Inspector 包装器问题

### 现象

- `Editor.log` 中这条错误在当前 `.sog4d` 导入之前就已经出现过多次.
- 它不仅会出现在 `.sog4d` 导入后,也会出现在无关的 prefab / HDRP 资源导入后.

### 静态证据

- 当前工程存在全局 Inspector/Hierarchy 扩展:
  - `Assets/vInspector`
  - `Assets/vHierarchy`
- 两个高风险包装点:
  - `Assets/vInspector/VInspectorComponentWindow.cs`
  - `Assets/vHierarchy/VHierarchyComponentWindow.cs`
- 它们都有相同结构:
  - `EditorGUILayout.BeginScrollView(...)`
  - `editor?.OnInspectorGUI()`
  - `EditorGUILayout.EndScrollView()`
- 但旧实现没有 `try/finally`,因此如果子 Inspector 提前 `ExitGUI`,ScrollView 结束调用会被跳过.

### 动态证据

- 修复前:
  - `Editor.log` 中 `EndLayoutGroup: BeginLayoutGroup must be called first.` 累计计数为 `9`.
- 修复后:
  - 触发一次 Unity `资产 > 刷新`
  - 等待脚本重编译与日志稳定
  - `Editor.log` 总行数从 `673098` 增长到 `673330`
  - 但该错误累计计数仍为 `9`,没有新增

### 当前结论

- 目前最有证据支持的根因不是 `wu.yize.gsplat` package 的自定义 Inspector 本身.
- 更像是工程里的 `vInspector / vHierarchy` 在包装其它 Inspector 时,因为 `ExitGUI` 提前退出导致布局栈泄漏.
