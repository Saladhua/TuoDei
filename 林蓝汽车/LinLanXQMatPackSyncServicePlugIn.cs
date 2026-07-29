using System;
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
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Web.Bill;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-物料审核同步包装方式到预置基础资料
    /// 物料审核通过后，将物料包装方式页签（QSGA_t_Cust_Entry100006）的数据
    /// 覆盖同步到预置基础资料 BAS_PreBaseDataOne（单据体 QSGA_Cust_Entry100009）
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-物料审核同步包装方式到预置基础资料")]
    public class LinLanXQMatPackSyncServicePlugIn : AbstractOperationServicePlugIn
    {
        /// <summary>
        /// 声明本次操作需要加载的字段
        /// </summary>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FNAME");
        }

        /// <summary>
        /// 事务提交后执行：遍历已审核物料，同步包装方式数据到预置基础资料
        /// </summary>
        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            // 遍历本次操作的每个已审核物料
            foreach (ExtendedDataEntity data in e.SelectedRows)
            {
                DynamicObject billObj = data.DataEntity;
                if (billObj == null) continue;

                long materialId = Convert.ToInt64(billObj["Id"]);
                if (materialId <= 0) continue;

                string materialName = ObjectToString(billObj["Name"]);
                DynamicObjectCollection packRows = QueryMaterialPackEntries(this.Context, materialId);

                SaveToPreBaseDataOne(this.Context, materialId, materialName, packRows);
            }
        }

        /// <summary>
        /// 查询物料包装方式页签数据
        /// </summary>
        private DynamicObjectCollection QueryMaterialPackEntries(Context ctx, long materialId)
        {
            string sql = string.Format(
                @"SELECT F_CustLI_PackName, F_CustLI_PackLength, F_CustLI_PackWidth,
                          F_CustLI_PackHeight, F_CustLI_PackWeight, F_CustLI_PackDesc
                   FROM QSGA_t_Cust_Entry100006
                   WHERE FMATERIALID = {0}
                   ORDER BY FEntryID",
                materialId);

            var dbService = ServiceFactory.GetDBService(ctx);
            return dbService.ExecuteDynamicObject(ctx, sql);
        }

        /// <summary>
        /// 保存数据到 BAS_PreBaseDataOne（通过 CreateBillView + BusinessDataServiceHelper 完整流程）
        /// </summary>
        private void SaveToPreBaseDataOne(Context ctx, long materialId, string materialName, DynamicObjectCollection packRows)
        {
            // 查询 BAS_PreBaseDataOne 是否已有该物料的记录
            string existSql = string.Format(
                "SELECT FID FROM T_BAS_PREBDONE WHERE F_CUSTLI_FMASTERID = {0}", materialId);
            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection existRows = dbService.ExecuteDynamicObject(ctx, existSql);

            long? existingFid = null;
            if (existRows != null && existRows.Count > 0)
            {
                existingFid = Convert.ToInt64(existRows[0]["FID"]);
            }

            // 通过 CreateBillView 创建单据视图
            BillView view = CreateBillView(ctx, "BAS_PreBaseDataOne", null, existingFid);

            // 触发 OnLoad 事件（填充默认值的关键步骤）
            DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
            proxy.FireOnLoad();

            // 引用字段用 SetItemValueByID（传物料内码字符串），普通字段用 SetValue
            view.Model.SetItemValueByID("F_CUSTLI_FMASTERID", materialId.ToString(), 0);
            view.Model.SetValue("Name", materialName, 0);
            view.InvokeFieldUpdateService("F_CUSTLI_FMASTERID", 0);
            view.InvokeFieldUpdateService("Name", 0);

            // 通过 Model.DataObject 取子表集合引用，清空后重新填充
            DynamicObjectCollection entryCol = view.Model.DataObject["QSGA_Cust_Entry100009"] as DynamicObjectCollection;
            entryCol.Clear();

            if (packRows != null && packRows.Count > 0)
            {
                foreach (DynamicObject row in packRows)
                {
                    DynamicObject entry = entryCol.DynamicCollectionItemPropertyType.CreateInstance() as DynamicObject;
                    entry["F_CustLI_PackName"] = ObjectToString(row["F_CustLI_PackName"]);
                    entry["F_CustLI_PackLength"] = ObjectToDecimal(row["F_CustLI_PackLength"]);
                    entry["F_CustLI_PackWidth"] = ObjectToDecimal(row["F_CustLI_PackWidth"]);
                    entry["F_CustLI_PackHeight"] = ObjectToDecimal(row["F_CustLI_PackHeight"]);
                    entry["F_CustLI_PackWeight"] = ObjectToDecimal(row["F_CustLI_PackWeight"]);
                    entry["F_CustLI_PackDesc"] = ObjectToString(row["F_CustLI_PackDesc"]);
                    entryCol.Add(entry);
                }
            }

            // 通过 Model 保存单据
            view.Model.Save();
        }

        /// <summary>
        /// 创建单据视图（供 SaveToPreBaseDataOne 使用的工具方法）
        /// 通过单据元数据初始化 BillView，加载数据后可通过 Model API 读写并保存
        /// </summary>
        private static BillView CreateBillView(Context ctx, string formId, string layoutId = null, object pkId = null)
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
