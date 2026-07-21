using System;
using System.ComponentModel;
using System.Data;
using System.Linq;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    [Obsolete("已废弃，改用Python插件 YxjTempPayableSave.py", false)]
    [Description("益讯机械-暂估应付单保存前自动生成付款计划(已废弃，改用Python插件)"), HotUpdate]
    public class YxjTempPayableSaveFormPlugIn : AbstractBillPlugIn
    {
        public override void DataChanged(DataChangedEventArgs e)
        {
            base.DataChanged(e);

            if (e.Field?.Key != "FSETACCOUNTTYPE")
                return;

            GeneratePaymentPlan();
        }

        public override void AfterBindData(EventArgs e)
        {
            base.AfterBindData(e);
            GeneratePaymentPlan();
        }

        private void GeneratePaymentPlan()
        {
            var bill = this.View.Model.DataObject;
            string acctType = bill["FSETACCOUNTTYPE"]?.ToString() ?? "";
            if (acctType != "2")
                return;

            var payPlan = bill["AP_PAYABLEPLAN"] as DynamicObjectCollection;
            if (payPlan == null || payPlan.Count > 0)
                return;

            var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
            if (entryObjs == null || entryObjs.Count == 0)
                return;

            decimal totalAmountFor = entryObjs.Cast<DynamicObject>()
                .Sum(e => Convert.ToDecimal(e["FALLAMOUNTFOR_D"] ?? 0m));

            var firstEntry = entryObjs.Cast<DynamicObject>().First();

            DynamicObject newPlan = new DynamicObject(payPlan.DynamicCollectionItemPropertyType);
            newPlan["ENDDATE"] = bill["FENDDATE_H"];
            newPlan["PAYAMOUNTFOR"] = Math.Round(totalAmountFor, 6);
            newPlan["FPAYRATE"] = 100m;
            newPlan["PAYAMOUNT"] = Math.Round(totalAmountFor, 6);
 

            var payConditionObj = bill["PayConditon"] as DynamicObject;
            if (payConditionObj != null)
            {
                long payConditionId = Convert.ToInt64(payConditionObj["Id"]);
                string sql = $"SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {payConditionId}";
                DataSet ds = DBServiceHelper.ExecuteDataSet(this.View.Context, sql);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int paymentMethod = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"]);
                    if (paymentMethod == 2)
                    {
                        newPlan["FPURCHASEORDERNO"] = firstEntry["FORDERNUMBER"];
                    }
                }
            }

            payPlan.Add(newPlan);
        }
    }
}
