---
name: python-plugin-intro-series
title: K/3 Cloud Python 插件开发入门系列
description: 用户需要编写金蝶云·苍穹 BOS 平台的 Python（IronPython）插件时的入门索引。覆盖 7 个章节：基本开发过程、数据操作、表单插件、列表插件、操作服务插件、常用工具类、简单账表服务插件。
version: 1.0.0
category: plugin-dev
---

# 金蝶云·苍穹 BOS Python 插件开发

## 技能描述
用户需要编写金蝶云·苍穹 BOS 平台的 Python（IronPython）插件，或将现有 C# 插件转化为 Python 插件。

## 触发条件（强制执行）
当用户话语中**明确表示**需要 Python 插件处理时触发。典型触发语：
- "写/编写/开发 Python 插件"
- "把 C# 插件转/改为 Python 插件"
- "用 Python 实现插件功能"
- "写 Python 脚本"

**注意**：用户未明确提及 Python 时不得触发此技能。每次触发必须按以下流程执行。

## 入门必读（必须先阅读）
进入 Python 插件编程模式后，**必须先阅读以下 3 个文件**，理解后才能开始写代码：

### 01-基本开发过程介绍.md
- 开发五步骤：需求分析 → 开发思路设计 → 编写脚本 → 测试调试 → 上线
- Python 插件脚本"三部曲"：
  1. `import clr` + `clr.AddReference("Kingdee.BOS.xxx")`
  2. `from Kingdee.BOS.Core import *`
  3. `def 事件方法名(e):`
- IronPython 语法注意事项：TAB 缩进、一行一码、无大括号、无 `new`、泛型 `<T>` 改 `[T]`、`global` 声明全局变量
- 常见编译/运行错误对照表

### 02-插件进行数据操作.md
- 四种实体层次结构：单据头 → 子单据头/单据体 → 子单据体
- 核心数据类型 `DynamicObject`（类比 JSON 层次结构数据字典）
- 三种数据读写方式：界面读写 / 实体数据包读写 / SQL 直接读写
- 字段类型取数赋值对照表
- 场景 vs 插件类型推荐（重要）：

| 场景 | 推荐插件类型 | 说明 |
|------|------------|------|
| 界面展示控制/交互（锁定、可见性、颜色等） | 表单插件 / 列表插件 | 依赖界面触发，操作 View 控件 |
| 保存/提交/审核时对数据进行赋值处理 | 操作服务插件 | 事务保护，支持批量，不依赖界面 |

### 06-插件常用工具类分享(完结篇).md
- `MetaDataServiceHelper` — 读取单据元数据
- `BusinessDataServiceHelper` — 读取/保存/提交/审核单据数据包
- `QueryServiceHelper` / `QueryBuilderParemeter` — 按需查询字段
- `DBUtils` 系列 — 执行 SQL（DataSet/DynamicObject/Execute/Batch/存储过程）
- 构建新单据：方法① 空白数据包 + Save / 方法② 构建 View
- `SystemParameterServiceHelper` / `UserParamterServiceHelper` / `PermissionServiceHelper`

## 插件类型选择（每次必须询问用户）
在编写代码前，**必须询问用户使用哪种插件类型**：

### 1. 表单插件（单据维护界面）
- 依赖 View，在单据编辑界面触发
- 适用于：控件状态、值更新、菜单点击、资料过滤、提示消息等
- 参考：`03-单据表单插件/` 完整模板和事件列表
- 常用事件：`AfterCreateModelData`、`AfterBindData`、`BarItemClick`、`DataChanged`、`BeforeF7Select`、`BeforeDoOperation`

### 2. 列表插件（列表界面）
- 依赖 ListView，在单据列表界面触发
- 适用于：列表过滤、批量操作、行双击、超链接、条件格式化（行颜色）
- 参考：`04-单据列表插件/` 完整示例和四种读数据方法
- 常用事件：`PrepareFilterParameter`、`AfterBarItemClick`、`ListRowDoubleClick`、`OnFormatRowConditions`

### 3. 操作服务插件（单据操作）
- APP 层插件，无 View，依赖操作触发
- **有事务保护**（`BeginOperationTransaction` / `EndOperationTransaction`），支持回滚
- 适用于：保存/提交/审核/删除等操作的业务逻辑处理、数据赋值
- 参考：`05-操作服务插件/` 完整架构和事件一览
- **必须实现 `OnPreparePropertys` 预加载字段！**
- 事件执行顺序：`OnPreparePropertys` → `OnAddValidators` → `BeginOperationTransaction`（事务内）→ `EndOperationTransaction`（事务内）→ `AfterExecuteOperationTransaction`

## 业务场景 → 推荐事件对照

| 业务场景 | 推荐插件类型 | 推荐事件/事务 |
|---------|------------|-------------|
| 新增单据时填充默认值 | 表单插件 | `AfterCreateModelData` |
| 设置字段锁定/可见性/颜色 | 表单插件 | `AfterBindData` |
| 自定义按钮点击功能 | 表单插件 | `BarItemClick` / `EntryBarItemClick` |
| 字段值变化后自动计算 | 表单插件 | `DataChanged` |
| F7 资料选择前动态过滤 | 表单插件 | `BeforeF7Select` |
| 列表自定义过滤条件 | 列表插件 | `PrepareFilterParameter` |
| 列表自定义菜单功能（批量处理） | 列表插件 | `AfterBarItemClick` |
| 列表行显示颜色标记 | 列表插件 | `OnFormatRowConditions` |
| 保存/提交/审核时对数据赋值 | 操作服务插件 | `BeginOperationTransaction`（事务内） |
| 操作完成后触发后续逻辑 | 操作服务插件 | `EndOperationTransaction`（事务内） |
| 自定义校验逻辑 | 操作服务插件 | `OnAddValidators` |
| 操作前预处理（非事务） | 操作服务插件 | `BeforeExecuteOperationTransaction` |
| 操作后处理（非事务，不影响结果） | 操作服务插件 | `AfterExecuteOperationTransaction` |

## 编程规则

### 1. 加载对应章节的完整模板
根据所选插件类型，引用对应的示例代码文件：
- 表单插件：`python-plugin-intro-series/03-单据表单插件/Python表单插件模板示例.txt`
- 列表插件：`python-plugin-intro-series/04-单据列表插件/列表插件示例代码.txt`
- 服务插件：`python-plugin-intro-series/05-操作服务插件/Python服务插件示例.txt`
- 工具类：`python-plugin-intro-series/06-插件常用工具类分享(完结篇)/常用工具类Python代码分享.txt`
- 简单账表服务插件：`python-plugin-intro-series/07-简单账表服务插件/07-简单账表服务插件.md`（完整代码见同目录 `采购日报表(动态列)取数服务插件示例.txt`）

### 2. 严格遵循 IronPython 规范
- `import clr` 必须在最顶部
- `clr.AddReference("程序集名称")` 引用 .NET DLL
- `from 命名空间 import *` 或导入具体类
- 事件方法名**大小写必须精确匹配**（如 `OnPreparePropertys` 不是 `OnPrepareProperties`）
- 使用 TAB 缩进（不要混合空格）
- 使用 `:` 标记代码块结束
- 使用 `def 方法名(e):` 定义事件
- 没有 `new` 关键字：`obj = DynamicObject(type)`
- 泛型 `<T>` 改为 `[T]`：`List[str]()` 而非 `List<string>()`
- 字符串格式化使用 `format()` 方法
- 布尔值为 `True` / `False`（首字母大写）
- 空判断使用 `is None` / `is not None`

### 3. 语法检查清单（输出代码前必须逐项排查）
- [ ] `import clr` 是否存在且在最顶部
- [ ] 所有 `clr.AddReference` 的 DLL 名称是否正确
- [ ] `from ... import *` 是否覆盖了使用的所有类
- [ ] 事件方法名大小写是否完全匹配（如 `AfterCreateModelData`）
- [ ] 方法定义末尾是否有英文冒号 `:`
- [ ] 缩进是否统一使用 TAB
- [ ] 全局变量是否在方法外声明并在方法内 `global` 引用
- [ ] 字符串是否使用 `""` 或 `''` 包裹
- [ ] 中文标点符号是否误用（`：` `；` `，` 等）
- [ ] 多行字符串是否使用 `"""..."""`
- [ ] 操作服务插件是否实现了 `OnPreparePropertys` 预加载字段
- [ ] `null` 比较是否使用 `is None` / `is not None`
- [ ] 泛型语法是否正确使用 `[T]` 而非 `<T>`

### 4. 常见错误排查参考
对照 `01-基本开发过程介绍.md` 中的"常见错误与注意事项"表：
- `unexpected indent` → 缩进问题，统一用 TAB
- `unexpected token '：'` / `'；'` → 有中文标点
- `global name 'DBServiceHelper' is not defined` → 缺少 `from Kingdee.BOS.ServiceHelper import *`
- `No module named Core` → 缺少 `clr.AddReference("Kingdee.BOS.Core")`
- `XXX is not Callable` → 去掉多余的 `()`
- 值类型不匹配错误 → 检查字段类型取数赋值对照表

## 配套资源索引

| 章节 | 内容 | 文件路径 |
|------|------|---------|
| 01 | 基本开发过程、语法注意事项、常见错误 | `python-plugin-intro-series/01-基本开发过程介绍/` |
| 02 | 数据操作、DynamicObject、字段类型对照 | `python-plugin-intro-series/02-插件进行数据操作/` |
| 03 | 表单插件模板、事件详解 | `python-plugin-intro-series/03-单据表单插件/` |
| 04 | 列表插件示例、四种读数据方法 | `python-plugin-intro-series/04-单据列表插件/` |
| 05 | 服务插件架构、事件一览、事务机制 | `python-plugin-intro-series/05-操作服务插件/` |
| 06 | 常用工具类大全、构建 View、SQL 操作 | `python-plugin-intro-series/06-插件常用工具类分享(完结篇)/` |
| 07 | 简单账表服务插件、取数插件开发、动态列构建 | `python-plugin-intro-series/07-简单账表服务插件/`（含完整示例代码 `.txt`） |
