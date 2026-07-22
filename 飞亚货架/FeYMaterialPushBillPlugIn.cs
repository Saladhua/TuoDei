using System;
using System.ComponentModel;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("飞亚货架-物料基础资料按钮手动推送至货架系统"), HotUpdate]
    public class FeYMaterialPushBillPlugIn : AbstractBillPlugIn
    {
        private const string BarItemKey = "tbPush";

        public override void AfterBarItemClick(AfterBarItemClickEventArgs e)
        {
            base.AfterBarItemClick(e);

            if (e.BarItemKey != BarItemKey) return;

            DynamicObject bill = this.View.Model.DataObject;
            if (bill == null) return;

            long fid = Convert.ToInt64(bill["Id"]);
            if (fid <= 0)
            {
                this.View.ShowMessage("物料尚未保存，请先保存再推送。");
                return;
            }

            string pushState = bill["F_CustLi_PushState"].ToString();
            if (pushState == "2")
            {
                this.View.ShowMessage("该物料已推送成功，无需重复推送。");
                return;
            }

            string number = bill["Number"].ToString();
            if (string.IsNullOrEmpty(number))
            {
                this.View.ShowMessage("物料编码为空，无法推送。");
                return;
            }

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
            string safeMessage = message == null ? "" : message.Replace("'", "''");

            string updateSql = string.Format(
                "UPDATE T_BD_MATERIAL SET F_CustLi_PushState = '{0}', F_CustLIRemark = '{1}' WHERE FMATERIALID = {2}",
                newState, safeMessage, fid);
            DBUtils.ExecuteDynamicObject(this.Context, updateSql);

            this.View.UpdateView("F_CustLi_PushState");
            this.View.UpdateView("F_CustLIRemark");

            if (success)
            {
                this.View.ShowMessage("推送成功：" + message);
            }
            else
            {
                this.View.ShowMessage("推送失败：" + message);
            }
        }
    }
}
