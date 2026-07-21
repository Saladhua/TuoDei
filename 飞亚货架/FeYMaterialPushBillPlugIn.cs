using System;
using System.ComponentModel;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("飞亚货架-物料基础资料按钮手动推送至货架系统"), HotUpdate]
    public class FeYMaterialPushBillPlugIn : AbstractBillPlugIn
    {
        private const string BarItemKey = "tbPush";

        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);

            if (e.BarItemKey != BarItemKey) return;

            DynamicObject bill = this.Model.DataObject;
            if (bill == null) return;

            long fid = Convert.ToInt64(bill["FID"] ?? 0L);
            if (fid <= 0)
            {
                this.View.ShowMessage("物料尚未保存，请先保存再推送。");
                return;
            }

            string number = bill["FNumber"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(number))
            {
                this.View.ShowMessage("物料编码为空，无法推送。");
                return;
            }

            string name = bill["FName"]?.ToString() ?? "";
            string spec = bill["FSpecification"]?.ToString() ?? "";

            string unitNumber = "";
            if (bill["FBaseUnitId"] is DynamicObject unitObj)
            {
                unitNumber = unitObj["FNumber"]?.ToString() ?? "";
            }

            string categoryName = "";
            if (bill["FMaterialGroup"] is DynamicObject catObj)
            {
                categoryName = catObj["FName"]?.ToString() ?? "";
            }

            var (success, message) = FeYHttpHelper.PushMaterial(
                number, name, spec, unitNumber, categoryName);

            string newState = success ? "2" : "3";
            string safeMessage = (message ?? "").Replace("'", "''");

            string updateSql = string.Format(
                "/* kingdee.CustLI.Business.PlugIn.FeYMaterialPushBillPlugIn */ UPDATE T_BD_MATERIAL SET F_CustLi_PushState = '{0}', F_CustLIRemark = '{1}' WHERE FID = {2}",
                newState, safeMessage, fid);

            DBUtils.Execute(this.Context, updateSql);

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
