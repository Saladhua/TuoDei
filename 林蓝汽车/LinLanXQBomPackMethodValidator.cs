using System;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    [System.ComponentModel.Description("林蓝汽车-物料清单-BOM包装方式查重（操作插件）")]
    public class LinLanXQBomPackMethodValidator : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("MATERIALID");
            e.FieldKeys.Add("F_CustLI_PackMethod");
        }

        public override void BeforeExecuteOperationTransaction(BeforeExecuteOperationTransaction e)
        {
            base.BeforeExecuteOperationTransaction(e);

            foreach (ExtendedDataEntity data in e.SelectedRows)
            {
                DynamicObject billObj = data.DataEntity;
                if (billObj == null) continue;

                long parentMaterialId = 0;
                long packMethodId = 0;

                if (billObj["MATERIALID"] != null)
                {
                    DynamicObject matObj = billObj["MATERIALID"] as DynamicObject;
                    if (matObj != null)
                    {
                        parentMaterialId = Convert.ToInt64(matObj["Id"]);
                    }
                }

                if (billObj["F_CustLI_PackMethod"] != null)
                {
                    DynamicObject packObj = billObj["F_CustLI_PackMethod"] as DynamicObject;
                    if (packObj != null)
                    {
                        packMethodId = Convert.ToInt64(packObj["Id"]);
                    }
                }

                if (packMethodId <= 0) continue;
                if (parentMaterialId <= 0) continue;

                if (CheckDuplicateBom(this.Context, parentMaterialId, packMethodId, billObj["Id"]))
                {
                    throw new Exception("父项物料编码 + 包装方式 已存在BOM版本，不允许重复创建。");
                }
            }
        }

        private bool CheckDuplicateBom(Context ctx, long parentMaterialId, long packMethodId, object currentBomId)
        {
            long currentId = 0;
            if (currentBomId != null)
            {
                long.TryParse(currentBomId.ToString(), out currentId);
            }

            StringBuilder sql = new StringBuilder();
            sql.AppendLine("SELECT COUNT(1) AS FCOUNT");
            sql.AppendLine("FROM T_ENG_BOM a1");
            sql.AppendLine("WHERE a1.FMATERIALID = " + parentMaterialId.ToString());
            sql.AppendLine("AND a1.F_CustLI_PackMethod = " + packMethodId.ToString());
            sql.AppendLine("AND a1.FDOCUMENTSTATUS IN ('A', 'C')");

            if (currentId > 0)
            {
                sql.AppendLine("AND a1.FID != " + currentId.ToString());
            }

            try
            {
                var dbService = ServiceFactory.GetDBService(ctx);
                DynamicObjectCollection result = dbService.ExecuteDynamicObject(ctx, sql.ToString());
                if (result != null && result.Count > 0)
                {
                    int count = Convert.ToInt32(result[0]["FCOUNT"]);
                    return count > 0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
