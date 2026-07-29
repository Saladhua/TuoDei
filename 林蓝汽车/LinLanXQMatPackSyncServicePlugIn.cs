using System;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-物料审核同步包装方式到预置基础资料
    /// 物料审核通过后，将物料包装方式页签（QSGA_t_Cust_Entry100006）的数据
    /// 覆盖同步到预置基础资料 BAS_PreBaseDataOne（子单据体 QSGA_Cust_Entry100009）
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

                string materialName = ObjectToString(billObj["NAME"]);
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
        /// 通过 BOS 标准 ISaveService 保存数据包到 BAS_PreBaseDataOne
        /// 已存在记录则设 Id 实现覆盖（UPDATE），不存在则 INSERT
        /// </summary>
        private void SaveToPreBaseDataOne(Context ctx, long materialId, string materialName, DynamicObjectCollection packRows)
        {
            FormMetadata meta = MetaDataServiceHelper.Load(ctx, "BAS_PreBaseDataOne") as FormMetadata;
            if (meta == null) return;

            var bi = meta.BusinessInfo;
            var headType = bi.GetDynamicObjectType();
            var entryItemType = bi.GetDynamicObjectType(true);

            // 查询 BAS_PreBaseDataOne 是否已有该物料的记录
            string existSql = string.Format(
                "SELECT FID FROM BAS_PreBaseDataOne WHERE F_CUSTLI_FMASTERID = {0}", materialId);
            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection existRows = dbService.ExecuteDynamicObject(ctx, existSql);

            DynamicObject headObj = new DynamicObject(headType);

            // 已有记录 → 设 Id 使 Save 变 UPDATE（覆盖）
            if (existRows != null && existRows.Count > 0)
            {
                headObj["Id"] = Convert.ToInt64(existRows[0]["FID"]);
            }
            // 无记录 → 不设 Id，Save 自动 INSERT

            headObj["F_CUSTLI_FMASTERID"] = materialId;
            headObj["FName"] = materialName;

            DynamicObjectCollection entryCollection = new DynamicObjectCollection(entryItemType, headObj);

            // 遍历物料包装方式页签名行，构建子表条目
            if (packRows != null && packRows.Count > 0)
            {
                foreach (DynamicObject row in packRows)
                {
                    DynamicObject entryObj = new DynamicObject(entryItemType);
                    entryObj["F_CustLI_PackName"] = ObjectToString(row["F_CustLI_PackName"]);
                    entryObj["F_CustLI_PackLength"] = ObjectToDecimal(row["F_CustLI_PackLength"]);
                    entryObj["F_CustLI_PackWidth"] = ObjectToDecimal(row["F_CustLI_PackWidth"]);
                    entryObj["F_CustLI_PackHeight"] = ObjectToDecimal(row["F_CustLI_PackHeight"]);
                    entryObj["F_CustLI_PackWeight"] = ObjectToDecimal(row["F_CustLI_PackWeight"]);
                    entryObj["F_CustLI_PackDesc"] = ObjectToString(row["F_CustLI_PackDesc"]);
                    entryCollection.Add(entryObj);
                }
            }

            // 将子表集合挂载到表头
            headObj["QSGA_Cust_Entry100009"] = entryCollection;

            // BOS 标准 API 保存（有 Id → UPDATE，无 Id → INSERT）
            ISaveService saveService = ServiceHelper.GetService<ISaveService>();
            OperateOption option = OperateOption.Create();
            saveService.Save(ctx, bi, new DynamicObject[] { headObj }, option, "Save");
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
