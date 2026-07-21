# 列表插件（AbstractListPlugIn）开发参考

## 适用场景
- 列表界面自定义过滤条件（`PrepareFilterParameter`）
- 列表行条件格式化/颜色标记（`OnFormatRowConditions`）
- **工具栏自定义按钮批量操作**（`BarItemClick` + `ListView.SelectedRowsInfo`）
- 行双击事件（`ListRowDoubleClick`）

## 基类
```csharp
using Kingdee.BOS.Core.List.PlugIn;

public class MyListPlugIn : AbstractListPlugIn
{
}
```

## 注册位置
列表插件注册在单据元数据的 `<ListPlugins>` 节点（BOS 设计器 → 列表插件）。

## 关键事件一览

| 事件 | 用途 |
|------|------|
| `BarItemClick(BarItemClickEventArgs e)` | 工具栏/菜单按钮点击 |
| `PrepareFilterParameter(FilterParameterEventArgs e)` | 列表查询前修改过滤条件 |
| `OnFormatRowConditions(FormatRowConditionsEventArgs e)` | 设置行显示颜色/字体 |
| `ListRowDoubleClick(ListRowDoubleClickEventArgs e)` | 行双击 |

## 实战案例：暂估应付单列表获取价目表价格

> 本案例来自益讯机械项目，文件 `YxjTempPayableGetPriceListPlugIn.cs`

### 功能描述
在暂估应付单列表界面增加"获取价目表价格"按钮，选中多行后批量从采购价目表取含税单价，填充并保存单据。

### 完整代码（含逐段说明）

```csharp
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.Operation;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using Kingdee.BOS.Web.Bill;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("益讯机械-暂估应付单列表获取价目表价格"), HotUpdate]
    public class YxjTempPayableGetPriceListPlugIn : AbstractListPlugIn
    {
        // ========== 常量定义 ==========
        private const string BarItemGetPrice = "tbGetPrice";  // 工具栏按钮标识
        private const string AcctTypeTemp = "2";               // 暂估应付
        private readonly HashSet<string> AllowDocStatus = new HashSet<string> { "Z", "A", "B", "D" };

        // ========== 按钮点击事件 ==========
        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);

            // Step 1: 只处理自定义按钮
            if (e.BarItemKey != BarItemGetPrice)
                return;

            // Step 2: 收集选中行数据
            // 按单据ID分组，存储分录ID集合
            Dictionary<string, List<string>> bills = new Dictionary<string, List<string>>();
            foreach (ListSelectedRow item in this.ListView.SelectedRowsInfo)
            {
                var billId = item.FieldValues["FBillHead"].ToString();
                var billEntryId = item.EntryPrimaryKeyValue;

                if (bills.Keys.Contains(billId))
                    bills[billId].Add(billEntryId);
                else
                    bills.Add(billId, new List<string> { billEntryId });
            }

            // Step 3: 逐单据处理
            foreach (var item in bills)
            {
                // 创建单据视图（编辑模式）
                var view = CreateBillView(this.Context, "AP_Payable", null, item.Key);

                // 遍历分录行
                var entryCount = view.Model.GetEntryRowCount("FEntityDetail");
                for (int i = 0; i < entryCount; i++)
                {
                    var entryId = view.Model.GetEntryPKValue("FEntityDetail", i).ToString();

                    // 只处理选中行
                    if (!item.Value.Any(x => x.Contains(entryId)))
                        continue;

                    // 获取物料
                    var matObj = view.Model.GetValue("FMATERIALID", i) as DynamicObject;
                    if (matObj == null) continue;
                    long materialId = Convert.ToInt64(matObj["Id"]);

                    // 获取供应商
                    var supplierObj = view.Model.GetValue("FSUPPLIERID") as DynamicObject;
                    if (supplierObj == null) continue;
                    long supplierId = Convert.ToInt64(supplierObj["Id"]);

                    // Step 4: 从价目表取最新含税单价
                    string sql = $@"
                        SELECT a.FTAXPRICE
                        FROM t_PUR_PriceListEntry a
                        INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                        WHERE a.FMATERIALID = {materialId}
                          AND b.FSUPPLIERID = {supplierId}
                          AND a.FDISABLESTATUS <> 'A'
                          AND a.FEFFECTIVEDATE <= GETDATE()
                        ORDER BY a.FEFFECTIVEDATE DESC";

                    var priceObjs = DBUtils.ExecuteDynamicObject(this.Context, sql);
                    decimal taxPrice = 0m;
                    if (priceObjs != null && priceObjs.Count > 0 && priceObjs[0]["FTAXPRICE"] != DBNull.Value)
                        taxPrice = Convert.ToDecimal(priceObjs[0]["FTAXPRICE"]);

                    // Step 5: 填入价格 + 触发值更新
                    view.Model.SetValue("FTAXPRICE", taxPrice, i);
                    view.InvokeFieldUpdateService("FTAXPRICE", i);
                }

                // Step 6: 保存单据
                view.Model.Save();
            }
        }

        // ========== 工具方法：创建单据视图 ==========
        private static BillView CreateBillView(Context ctx, string formId, 
            string layoutId = null, object pkId = null)
        {
            var meta = (FormMetadata)MetaDataServiceHelper.Load(ctx, formId);
            var form = meta.BusinessInfo.GetForm();

            var param = new BillOpenParameter(formId, layoutId);
            param.Context = ctx;
            param.FormMetaData = meta;
            if (pkId != null && !string.IsNullOrWhiteSpace(pkId.ToString()))
            {
                param.Status = OperationStatus.EDIT;
                param.InitStatus = OperationStatus.EDIT;
                param.PkValue = pkId;
            }
            else
            {
                param.Status = OperationStatus.ADDNEW;
                param.InitStatus = OperationStatus.ADDNEW;
            }

            param.SetCustomParameter("formID", form.Id);
            param.SetCustomParameter("PlugIns", form.CreateFormPlugIns());
            param.SetCustomParameter("ShowConfirmDialogWhenChangeOrg", false);
            param.NetCtrlDisable = true;

            var provider = form.GetFormServiceProvider();
            var billview = (BillView)provider.GetService(typeof(IDynamicFormView));
            billview.Initialize(param, provider);
            billview.LoadData();
            return billview;
        }
    }
}
```

### 关键要点

| 要点 | 说明 |
|------|------|
| `BarItemClick` | 拦截工具栏按钮点击，通过 `e.BarItemKey` 区分按钮 |
| `ListView.SelectedRowsInfo` | 获取列表选中行集合，`FBillHead`=单据ID，`EntryPrimaryKeyValue`=分录ID |
| `CreateBillView` | 创建单据编辑视图，需要加载元数据 + 初始化参数 + 加载数据 |
| `Model.GetValue/SetValue` | 读写单据体字段值，第二个参数为行索引 |
| `Model.Save()` | 持久化单据数据 |
| `InvokeFieldUpdateService` | 触发值更新服务（联动刷新相关字段） |

### 注意事项
1. **性能**：逐单创建 BillView + Save 在批量大时较慢，可考虑批量 SQL 替代
2. **SQL 安全**：示例中用字符串拼接，建议改用参数化查询
3. **事务**：多个单据循环处理没有整体事务包装，中间失败不会回滚前面已保存的单据
4. **字段声明**：列表插件不走 `OnPreparePropertys`，而是通过 `SelectorItemInfo` 或 `BillView` 加载字段
5. **状态过滤**：建议在处理前先校验单据状态，避免已审核/已关闭的单据被修改
