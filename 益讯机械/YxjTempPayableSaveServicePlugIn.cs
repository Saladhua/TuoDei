using System;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("益讯机械-暂估应付单保存自动生成付款计划"), HotUpdate]
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
        }

        public override void BeforeExecuteOperationTransaction(BeforeExecuteOperationTransaction e)
        {
            base.BeforeExecuteOperationTransaction(e);

            foreach (DynamicObject bill in e.DataEntitys)
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

                DynamicObject newPlan = new DynamicObject(payPlan.DynamicCollectionItemPropertyType);
                newPlan["ENDDATE"] = bill["FENDDATE_H"];
                newPlan["PAYAMOUNTFOR"] = Math.Round(totalAmountFor, 6);
                newPlan["FPAYRATE"] = 100m;
                newPlan["PAYAMOUNT"] = Math.Round(totalAmountFor, 6);
                payPlan.Add(newPlan);
            }
        }
    }
}
