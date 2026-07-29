using System;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

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
        /// 保存数据到 BAS_PreBaseDataOne（通过 ISaveService 完整流程触发单据编号生成与默认值初始化）
        /// </summary>
        private void SaveToPreBaseDataOne(Context ctx, long materialId, string materialName, DynamicObjectCollection packRows)
        {
            FormMetadata meta = MetaDataServiceHelper.Load(ctx, "BAS_PreBaseDataOne") as FormMetadata;
            if (meta == null) return;

            var billType = meta.BusinessInfo.GetDynamicObjectType();

            // 获取物料引用类型
            FormMetadata matMeta = MetaDataServiceHelper.Load(ctx, "BD_MATERIAL") as FormMetadata;
            if (matMeta == null) return;
            var matType = matMeta.BusinessInfo.GetDynamicObjectType();

            // 查询 BAS_PreBaseDataOne 是否已有该物料的记录
            string existSql = string.Format(
                "SELECT FID FROM T_BAS_PREBDONE WHERE F_CUSTLI_FMASTERID = {0}", materialId);
            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection existRows = dbService.ExecuteDynamicObject(ctx, existSql);

            DynamicObject bill;
            if (existRows != null && existRows.Count > 0)
            {
                bill = BusinessDataServiceHelper.LoadSingle(ctx,
                    Convert.ToInt64(existRows[0]["FID"]),
                    billType) as DynamicObject;
            }
            else
            {
                bill = billType.CreateInstance() as DynamicObject;
            }

            // 表头赋值（通过LoadSingle获取完整实体，并设_Id以便外键正确解析）
            DynamicObject matRef = BusinessDataServiceHelper.LoadSingle(ctx, materialId, matType) as DynamicObject;
            if (matRef != null)
            {
                bill["F_CUSTLI_FMASTERID"] = matRef;
                bill["F_CUSTLI_FMASTERID_Id"] = materialId;
            }
            bill["Name"] = materialName;

            // 取单据体集合引用（不直接赋值，绕过只读检查）
            DynamicObjectCollection entryCol = bill["QSGA_Cust_Entry100009"] as DynamicObjectCollection;
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

            // 通过 ISaveService 完整流程保存，触发表单编号生成与默认值初始化
            ISaveService saveService = ServiceHelper.GetService<ISaveService>();
            saveService.Save(ctx, meta.BusinessInfo, new DynamicObject[] { bill });
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
