using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.DynamicForm;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 财务应付单 - 提交时校验"发票金额 与 采购价目表金额"是否一致（益讯机械）
    /// 注册位置：AP_Payable 的 &lt;OperationServicePlugins&gt;，OperationName = "Submit"。
    /// 触发时机：财务应付单（采购发票）提交时（提交前校验）。
    /// 业务规则：逐行比较 发票含税单价(FTAXPRICE) 与 价目表含税单价(F_CustLi_PriceListTaxPrice)；
    ///          不一致时弹出"提醒框"（非阻断，仅提示采购员更新采购价目表）。
    /// 仅对 立账类型 = 财务(3) 生效。
    /// </summary>
    [Description("益讯机械-财务应付单提交校验价目表一致性"), HotUpdate]
    public class YxjAPPayableSubmitValidatePlugIn : AbstractOperationServicePlugIn
    {
        // 立账类型：3 = 财务
        private const string AcctTypeFinance = "3";

        // 允许的尾差（金额极小误差视为一致）
        private const decimal Tolerance = 0.0001m;

        /// <summary>
        /// 声明本次操作需要加载的字段
        /// </summary>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);

            e.FieldKeys.Add("FSETACCOUNTTYPE");            // 立账类型：区分暂估/财务
            e.FieldKeys.Add("AP_PAYABLEENTRY");           // 单据体
            e.FieldKeys.Add("AP_PAYABLEENTRY.TaxPrice"); // 发票含税单价
            e.FieldKeys.Add("F_CustLi_PriceListTaxPrice"); // 价目表含税单价
        }

        /// <summary>
        /// 操作执行完成后（事务已提交）：添加非阻断的"不一致提醒"
        /// 说明：仅作提醒不阻断提交，故在 AfterExecuteOperationTransaction 中追加 OperateResult。
        /// </summary>
        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            foreach (DynamicObject bill in e.DataEntitys)
            {
                // 仅财务应付单（立账类型 = 3）
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeFinance)
                {
                    continue;
                }

                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null)
                {
                    continue;
                }

                // 单据编号（用于提醒信息展示）
                string billNo = ObjectUtils.Object2String(
                    this.BusinessInfo.GetBillNoField().DynamicProperty.GetValueFast(bill));
                object pkValue = bill["Id"];

                foreach (DynamicObject entry in entryObjs)
                {
                    decimal invoicePrice = (entry["TaxPrice"] == null) ? 0m : Convert.ToDecimal(entry["TaxPrice"]);
                    decimal listPrice = (entry["F_CustLi_PriceListTaxPrice"] == null)
                        ? 0m
                        : Convert.ToDecimal(entry["F_CustLi_PriceListTaxPrice"]);

                    // 考虑尾差，绝对值小于容差视为一致
                    if (Math.Abs(invoicePrice - listPrice) > Tolerance)
                    {
                        // 非阻断提醒：SuccessStatus = true，仅提示采购员
                        var result = new OperateResult();
                        result.SuccessStatus = true;
                        result.Name = "价目表不一致提醒";
                        result.PKValue = pkValue;
                        result.Number = billNo;
                        result.Message = string.Format(
                            "单据[{0}] 第 {1} 行：发票含税单价 {2} 与价目表含税单价 {3} 不一致，请确认是否需要更新采购价目表。",
                            billNo,
                            entry["SEQ"],
                            invoicePrice,
                            listPrice);

                        this.OperationResult.OperateResult.Add(result);
                    }
                }
            }

            // 确保提醒信息在客户端弹出
            this.OperationResult.IsShowMessage = true;
        }
    }
}
