using System;
using System.ComponentModel;
using System.Data;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    [Obsolete("已废弃，改用 YxjTempPayableSaveFormPlugIn（表单插件 BeforeSave 方式）", false)]
    [Description("益讯机械-暂估应付单保存自动生成付款计划(已废弃，改用表单插件)"), HotUpdate]
    public class YxjTempPayableSaveServicePlugIn : AbstractOperationServicePlugIn
    {
        private const string AcctTypeTemp = "2";

        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FSETACCOUNTTYPE");
            e.FieldKeys.Add("FENDDATE_H");
            e.FieldKeys.Add("AP_PAYABLEPLAN");
            e.FieldKeys.Add("AP_PAYABLEENTRY");
            e.FieldKeys.Add("FALLAMOUNTFOR_D");
            e.FieldKeys.Add("FPAYCONDITION");
            e.FieldKeys.Add("FORDERNUMBER");
            e.FieldKeys.Add("FPURCHASEORDERNO");
        }

        public override void BeforeExecuteOperationTransaction(BeforeExecuteOperationTransaction e)
        {
            base.BeforeExecuteOperationTransaction(e);

            foreach (var bill in e.DataEntitys)
            {
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeTemp)
                    continue;

                var payPlan = bill["AP_PAYABLEPLAN"] as DynamicObjectCollection;
                if (payPlan == null || payPlan.Count > 0)
                    continue;

                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null || entryObjs.Count == 0)
                    continue;

                decimal totalAmountFor = 0m;
                foreach (DynamicObject entry in entryObjs)
                {
                    totalAmountFor += Convert.ToDecimal(entry["FALLAMOUNTFOR_D"] ?? 0m);
                }

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
                    DataSet ds = DBServiceHelper.ExecuteDataSet(this.Context, sql);
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
}
