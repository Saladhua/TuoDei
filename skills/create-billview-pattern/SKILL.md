# CreateNewBillView 模式 · 操作服务插件编程式创建单据视图

## 适用场景

在 `AbstractOperationServicePlugIn`（或任意 `Context` 可用处）中，通过编程方式创建单据视图、填充数据、执行 Save/Submit/Audit。

常见于：审核同步、下推写入、跨单据复制、审批操作中自动创建关联单据。

## 核心流程

```
Initialize → LoadData() → FireOnLoad() → 设字段值 → Save → (可选)Submit → (可选)Audit
```

## 必须导入的命名空间

```csharp
using Kingdee.BOS.Core.Bill;                    // IBillView, BillOpenParameter
using Kingdee.BOS.Core.DynamicForm;              // IDynamicFormView, IResourceServiceProvider, IDynamicFormViewService
using Kingdee.BOS.Core.DynamicForm.PlugIn;       // AbstractDynamicFormPlugIn, FormConst
using Kingdee.BOS.Core.Metadata;                 // FormMetadata, CreateFrom
using Kingdee.BOS.Orm;                           // OperateOption ⚠️ 易遗漏
using Kingdee.BOS.Web.Bill;                      // BillView
```

## 方法模板

```csharp
private IBillView CreateNewBillView(Context ctx, string formId, object pkId = null)
{
    FormMetadata meta = MetaDataServiceHelper.Load(ctx, formId) as FormMetadata;
    if (meta == null)
        throw new Exception(string.Format("未能加载单据元数据，FormId={0}", formId));

    var form = meta.BusinessInfo.GetForm();

    IResourceServiceProvider formServiceProvider = form.GetFormServiceProvider(true);
    IDynamicFormViewService billViewService =
        formServiceProvider.GetService(typeof(IDynamicFormView)) as IDynamicFormViewService;

    BillOpenParameter openParam = new BillOpenParameter(form.Id, string.Empty);
    openParam.Context = ctx;
    openParam.ServiceName = form.FormServiceName;
    openParam.PageId = Guid.NewGuid().ToString();
    openParam.FormMetaData = meta;
    openParam.CreateFrom = CreateFrom.Default;
    openParam.ParentId = 0;
    openParam.GroupId = "";
    openParam.SetCustomParameter("ShowConfirmDialogWhenChangeOrg", false);

    List<AbstractDynamicFormPlugIn> plugs = form.CreateFormPlugIns();
    openParam.SetCustomParameter(FormConst.PlugIns, plugs);

    if (pkId != null)
    {
        openParam.Status = OperationStatus.EDIT;
        openParam.InitStatus = OperationStatus.EDIT;
        openParam.PkValue = pkId;
    }
    else
    {
        openParam.Status = OperationStatus.ADDNEW;
        openParam.PkValue = null;
    }

    billViewService.Initialize(openParam, formServiceProvider);

    IBillView view = (IBillView)billViewService;

    // 无论 ADDNEW 还是 EDIT 都统一 LoadData()
    // 某些表单用 CreateNewModelData() 会导致数据初始化不完整
    ((BillView)view).LoadData();

    return view;
}
```

## 字段设值 · 红线规则

### 基础资料引用字段
```csharp
view.Model.SetItemValueByID("字段标识", 内码字符串, 0);
view.InvokeFieldUpdateService("字段标识", 0);
```
必须紧跟 `InvokeFieldUpdateService` 触发联动。

### 文本/数值/复选框等普通字段
```csharp
// ❌ 不要这样：
view.Model.SetValue("字段名", value);                // SetValue 可能失效
view.InvokeFieldUpdateService("字段名", 0);           // ❌ 文本字段禁止调用此方法

// ✅ 要这样：
view.Model.DataObject["字段名"] = value;              // 直接写 DataObject 最可靠
```
- 直接写 `DataObject["字段名"] = value`，**不调用** `InvokeFieldUpdateService`
- 普通字段的 `InvokeFieldUpdateService` 会触发联动公式/插件，重置字段内容

### 执行顺序（最重要的红线）
```csharp
// ✅ 正确顺序
view.Model.SetItemValueByID("F_CUSTLI_FMASTERID", id.ToString(), 0);
view.InvokeFieldUpdateService("F_CUSTLI_FMASTERID", 0);   // 先触联动
view.Model.DataObject["Name"] = materialName;               // 联动了再设 Name，不被覆盖

// ❌ 错误顺序
view.Model.SetValue("Name", materialName);
view.InvokeFieldUpdateService("F_CUSTLI_FMASTERID", 0);    // ← 联动会把 Name 覆盖为空

// ❌ 错误用法
view.Model.SetValue("Name", materialName, 0);
view.InvokeFieldUpdateService("Name", 0);                  // ← 文本字段禁止 InvokeFieldUpdateService
```

## 完整流程示例（新增 + Save + Submit + Audit）

```csharp
IBillView view = CreateNewBillView(ctx, "表单ID", null);

DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
proxy.FireOnLoad();

view.Model.SetItemValueByID("F_SomeBaseField", baseId.ToString(), 0);
view.InvokeFieldUpdateService("F_SomeBaseField", 0);

view.Model.DataObject["F_SomeTextField"] = textValue;
view.Model.DataObject["Name"] = nameValue;

DynamicObjectCollection entryCol = view.Model.DataObject["EntryEntityKey"] as DynamicObjectCollection;
entryCol.Clear();
foreach (var row in sourceRows)
{
    DynamicObject entry = entryCol.DynamicCollectionItemPropertyType.CreateInstance() as DynamicObject;
    entry["F_Field1"] = value1;
    entryCol.Add(entry);
}

IOperationResult saveResult = BusinessDataServiceHelper.Save(
    ctx, view.BillBusinessInfo, view.Model.DataObject,
    OperateOption.Create(), "Save");
if (!saveResult.IsSuccess) { /* throw */ }

long savedId = Convert.ToInt64(view.Model.DataObject["Id"]);
object[] ids = new object[] { savedId };

IOperationResult submitResult = BusinessDataServiceHelper.Submit(
    ctx, view.BillBusinessInfo, ids, "Submit", OperateOption.Create());
if (!submitResult.IsSuccess) { /* throw */ }

IOperationResult auditResult = BusinessDataServiceHelper.Audit(
    ctx, view.BillBusinessInfo, ids, OperateOption.Create());
if (!auditResult.IsSuccess) { /* throw */ }
```

## 类型陷阱一览

| 预期类型 | 实际位置 | 说明 |
|---|---|---|
| `OperateOption` | `Kingdee.BOS.Orm.OperateOption` | 非 `Core.DynamicForm.Operation` |
| `IBillView` | `Kingdee.BOS.Core.Bill.IBillView` | 非 `Web.Bill` |
| `IDynamicFormViewService` | `Kingdee.BOS.Core.DynamicForm` | 在 Core.dll |
| `Form` (元数据) | `Kingdee.BOS.Core.Metadata.FormElement.Form` | 嵌套类型，只能用 `var` |
| `FormConst` | `Kingdee.BOS.Core.FormConst` | 非 `DynamicForm.PlugIn` |
| `CreateFrom` | `Kingdee.BOS.Core.Metadata.CreateFrom` | 在 Core.dll |
| `IResourceServiceProvider` | `Kingdee.BOS.Core.DynamicForm` | 在 Core.dll |

## 明细单据体双 Key（吉茂 2026-08-04 复盘四实证）

`CreateNewEntryRow/GetEntryRowCount` 与 `DataObject["..."]` 使用的是**两个不同的 key**：

| API | 参数类型 | 示例 |
|---|---|---|
| `view.Model.CreateNewEntryRow(entryKey)` / `GetEntryRowCount(entryKey)` | 元数据**实体键**（带 F 前缀） | `"FSaleOrderEntry"` |
| `view.Model.DataObject["..."]`（取明细集合） | **集合属性名**（不带 F 前缀） | `"SaleOrderEntry"` |

禁止把两者当作同一 key：
- `DataObject["FSaleOrderEntry"]` → 抛"实体类型 SaleOrder 不存在该属性"
- `CreateNewEntryRow("SaleOrderEntry")` → 抛"未将对象引用设置到对象的实例"（空引用）

推荐拆两个常量分别使用；`DataObject` 的集合属性名以林蓝等实证为准（如销售订单为 `SaleOrderEntry`）。
