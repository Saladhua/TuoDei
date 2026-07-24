using System;
using System.ComponentModel;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 物料审核通过后自动推送至货架系统的操作插件
    /// </summary>
    [Description("飞亚货架-物料审核通过后自动推送至货架系统"), HotUpdate]
    public class FeYMaterialPushServicePlugIn : AbstractOperationServicePlugIn
    {
        /// <summary>
        /// 声明推送所需的字段
        /// </summary>
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

        /// <summary>
        /// 审核事务提交后，遍历物料数据推送至货架系统
        /// </summary>
        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            foreach (DynamicObject bill in e.DataEntitys)
            {
                if (bill == null) continue;

                long fid = Convert.ToInt64(bill["Id"]);                 // 物料内码
                if (fid <= 0) continue;

                string pushState = bill["F_CustLi_PushState"].ToString(); // 推送状态：2=已成功
                if (pushState == "2") continue;

                string number = bill["Number"].ToString();           // 物料编码
                if (string.IsNullOrEmpty(number)) continue;

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
            }
        }
    }
}
