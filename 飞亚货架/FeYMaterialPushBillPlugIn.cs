using System;
using System.ComponentModel;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 物料基础资料按钮手动推送至货架系统的表单插件
    /// </summary>
    [Description("飞亚货架-物料基础资料按钮手动推送至货架系统"), HotUpdate]
    public class FeYMaterialPushBillPlugIn : AbstractBillPlugIn
    {
        private const string BarItemKey = "tbPush";

        /// <summary>
        /// 工具栏按钮点击事件，手动推送物料至货架系统
        /// </summary>
        public override void AfterBarItemClick(AfterBarItemClickEventArgs e)
        {
            base.AfterBarItemClick(e);

            // 非推送按钮直接跳过
            if (e.BarItemKey != BarItemKey) return;

            DynamicObject bill = this.View.Model.DataObject;
            if (bill == null) return;

            // 物料未保存时禁止推送
            long fid = Convert.ToInt64(bill["Id"]);                 // 物料内码
            if (fid <= 0)
            {
                this.View.ShowMessage("物料尚未保存，请先保存再推送。");
                return;
            }

            string pushState = bill["F_CustLi_PushState"].ToString(); // 推送状态：2=已成功
            if (pushState == "2")
            {
                this.View.ShowMessage("该物料已推送成功，无需重复推送。");
                return;
            }

            string number = bill["Number"].ToString();           // 物料编码
            if (string.IsNullOrEmpty(number))
            {
                this.View.ShowMessage("物料编码为空，无法推送。");
                return;
            }

            string name = bill["Name"].ToString();               // 物料名称
            string spec = bill["Specification"].ToString();      // 规格型号

            // 取基本单位编码
            string unitNumber = "";
            if (bill["MaterialBase"] is DynamicObjectCollection matBaseColl && matBaseColl.Count > 0)
            {
                if (matBaseColl[0]["BaseUnitId"] is DynamicObject unitObj)
                {
                    unitNumber = unitObj["Number"].ToString();   // 单位编码
                }
            }

            // 取物料分类名称
            string categoryName = "";
            if (bill["MaterialGroup"] is DynamicObject catObj)
            {
                categoryName = catObj["Name"].ToString();        // 物料分类名称
            }

            // 调用货架接口推送物料
            var (success, message) = FeYHttpHelper.PushMaterial(
                number, name, spec, unitNumber, categoryName);

            // 更新推送状态：2=成功 3=失败
            string newState = success ? "2" : "3";
            string safeMessage = message == null ? "" : message.Replace("'", "''");

            // 回写物料基础资料自定义字段：推送状态+结果备注
            string updateSql = string.Format(
                "UPDATE T_BD_MATERIAL SET F_CustLi_PushState = '{0}', F_CustLIRemark = '{1}' WHERE FMATERIALID = {2}",
                newState, safeMessage, fid);
            DBUtils.ExecuteDynamicObject(this.Context, updateSql);

            // 刷新界面字段显示
            this.View.UpdateView("F_CustLi_PushState");
            this.View.UpdateView("F_CustLIRemark");
        }
    }
}
