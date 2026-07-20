using System;
using System.ComponentModel;
using System.Data;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Validation;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
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
            e.FieldKeys.Add("FPAYCONDITION");
            e.FieldKeys.Add("FORDERNUMBER");
            e.FieldKeys.Add("FPURCHASEORDERNO");
        }

        public override void OnAddValidators(AddValidatorsEventArgs e)
        {
            base.OnAddValidators(e);
            e.Validators.Insert(0, new PaymentPlanValidator());
        }

        private class PaymentPlanValidator : AbstractValidator
        {
            public PaymentPlanValidator()
            {
                this.EntityKey = "FBillHead";
                this.AlwaysValidate = true;
            }

            public override void Validate(
                ExtendedDataEntity[] dataEntities,
                ValidateContext validateContext,
                Context ctx)
            {
                foreach (var entity in dataEntities)
                {
                    DynamicObject bill = entity.DataEntity;

                    string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                    if (acctType != YxjTempPayableSaveServicePlugIn.AcctTypeTemp)
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

                    var payConditionObj = bill["PayConditon"] as DynamicObject;
                    if (payConditionObj != null)
                    {
                        long payConditionId = Convert.ToInt64(payConditionObj["Id"]);
                        string sql = $"SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {payConditionId}";
                        DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                        {
                            int paymentMethod = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"]);
                            if (paymentMethod == 2)
                            {
                                var firstEntry = entryObjs.Cast<DynamicObject>().First();
                                newPlan["FPURCHASEORDERNO"] = firstEntry["FORDERNUMBER"];
                            }
                        }
                    }

                    payPlan.Add(newPlan);
                }
            }
        }
    }
}
