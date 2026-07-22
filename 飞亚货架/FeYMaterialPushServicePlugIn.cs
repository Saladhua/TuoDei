using System;
using System.ComponentModel;
using Kingdee.BOS.Core;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("飞亚货架-物料审核通过后自动推送至货架系统"), HotUpdate]
    public class FeYMaterialPushServicePlugIn : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FNumber");
            e.FieldKeys.Add("FName");
            e.FieldKeys.Add("FSpecification");
            e.FieldKeys.Add("MaterialBase");
            e.FieldKeys.Add("FMaterialGroup");
            e.FieldKeys.Add("F_CustLi_PushState");
            e.FieldKeys.Add("F_CustLIRemark");
        }

        public override void EndOperationTransaction(EndOperationTransactionArgs e)
        {
            base.EndOperationTransaction(e);

            foreach (DynamicObject bill in e.DataEntitys)
            {
                if (bill == null) continue;

                long fid = Convert.ToInt64(bill["ID"]);
                if (fid <= 0) continue;

                string pushState = bill["F_CustLi_PushState"].ToString();
                if (pushState == "2") continue;

                string number = bill["Number"].ToString();
                if (string.IsNullOrEmpty(number)) continue;

                string name = bill["Name"].ToString();
                string spec = bill["Specification"].ToString();

                string unitNumber = "";
                if (bill["MaterialBase"] is DynamicObjectCollection matBaseColl && matBaseColl.Count > 0)
                {
                    if (matBaseColl[0]["BaseUnitId"] is DynamicObject unitObj)
                    {
                        unitNumber = unitObj["Number"].ToString();
                    }
                }

                string categoryName = "";
                if (bill["MaterialGroup"] is DynamicObject catObj)
                {
                    categoryName = catObj["Name"].ToString();
                }

                var (success, message) = FeYHttpHelper.PushMaterial(
                    number, name, spec, unitNumber, categoryName);

                string newState = success ? "2" : "3";
                string safeMessage = (message ?? "").Replace("'", "''");

                string updateSql = string.Format(
                    "UPDATE T_BD_MATERIAL SET F_CustLi_PushState = '{0}', F_CustLIRemark = '{1}' WHERE FMATERIALID = {2}",
                    newState, safeMessage, fid);
                DBUtils.ExecuteDynamicObject(this.Context, updateSql);
            }
        }
    }
}
