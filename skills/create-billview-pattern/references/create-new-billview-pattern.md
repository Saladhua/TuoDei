# CreateNewBillView 完整参考代码

## 完整实现

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.Operation;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Web.Bill;

namespace kingdee.CustLI.Business.PlugIn
{
    [System.ComponentModel.Description("示例")]
    public class ExampleServicePlugIn : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FNAME");
        }

        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            foreach (ExtendedDataEntity data in e.SelectedRows)
            {
                DynamicObject billObj = data.DataEntity;
                if (billObj == null) continue;

                long materialId = Convert.ToInt64(billObj["Id"]);
                if (materialId <= 0) continue;

                string materialName = ObjectToString(billObj["Name"]);

                SaveToTargetForm(this.Context, materialId, materialName);
            }
        }

        private void SaveToTargetForm(Context ctx, long materialId, string materialName)
        {
            bool isNewRecord = true;

            IBillView view = CreateNewBillView(ctx, "目标表单ID", null);

            DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
            proxy.FireOnLoad();

            // 基础资料字段：SetItemValueByID + InvokeFieldUpdateService
            view.Model.SetItemValueByID("F_CUSTLI_FMASTERID", materialId.ToString(), 0);
            view.InvokeFieldUpdateService("F_CUSTLI_FMASTERID", 0);

            // 文本字段：直接写 DataObject，不调用 InvokeFieldUpdateService
            // 位置必须在所有 InvokeFieldUpdateService 之后
            view.Model.DataObject["Name"] = materialName;

            // Save
            IOperationResult saveResult = BusinessDataServiceHelper.Save(
                ctx, view.BillBusinessInfo, view.Model.DataObject,
                OperateOption.Create(), "Save");

            if (!saveResult.IsSuccess)
            {
                var errMsgs = saveResult.ValidationErrors.Select(x => x.Message);
                throw new Exception(
                    string.Format("保存失败：{0}", string.Join(",", errMsgs)));
            }

            // Submit + Audit（仅新增）
            if (isNewRecord)
            {
                long savedId = Convert.ToInt64(view.Model.DataObject["Id"]);
                object[] ids = new object[] { savedId };

                IOperationResult submitResult = BusinessDataServiceHelper.Submit(
                    ctx, view.BillBusinessInfo, ids, "Submit", OperateOption.Create());

                if (!submitResult.IsSuccess)
                {
                    var errMsgs = submitResult.ValidationErrors.Select(x => x.Message);
                    throw new Exception(
                        string.Format("提交失败：{0}", string.Join(",", errMsgs)));
                }

                IOperationResult auditResult = BusinessDataServiceHelper.Audit(
                    ctx, view.BillBusinessInfo, ids, OperateOption.Create());

                if (!auditResult.IsSuccess)
                {
                    var errMsgs = auditResult.ValidationErrors.Select(x => x.Message);
                    throw new Exception(
                        string.Format("审核失败：{0}", string.Join(",", errMsgs)));
                }
            }
        }

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
            ((BillView)view).LoadData();

            return view;
        }

        private string ObjectToString(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return value.ToString();
        }

        private decimal ObjectToDecimal(object value)
        {
            if (value == null || value == DBNull.Value) return 0m;
            decimal result;
            decimal.TryParse(value.ToString(), out result);
            return result;
        }
    }
}
```

## 关键调试记录

### 2026-07-29 事故复盘

**问题**：`BAS_PreBaseDataOne` 保存失败，`Name` 字段必填。

**根因1**：文本字段调用了 `InvokeFieldUpdateService`。
```
view.Model.SetValue("Name", materialName, 0);
view.InvokeFieldUpdateService("Name", 0);         // ← 此行为将 Name 重置为空
```

**根因2**：设值顺序错误。`InvokeFieldUpdateService` 触发的联动覆盖了之前设的 Name 值。
```
// ❌ Name 设在前，联动在后，联动把 Name 覆盖
SetValue("Name", ...) → InvokeFieldUpdateService("关联字段", ...)

// ✅ 联动在前，Name 在后，联动完再设 Name 不会被覆盖
InvokeFieldUpdateService("关联字段", ...) → SetValue / DataObject["Name"]
```

**根因3**：`CreateNewModelData()` 对某些表单不适用，统一用 `LoadData()` 更可靠。

**命名空间陷阱**：`OperateOption` 在 `Kingdee.BOS.Orm` 而非 `Kingdee.BOS.Core.DynamicForm.Operation`，缺少 `using Kingdee.BOS.Orm;` 会导致 CS0103 编译错误。
