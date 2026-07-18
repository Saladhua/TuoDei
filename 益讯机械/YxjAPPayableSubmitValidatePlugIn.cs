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
            e.FieldKeys.Add("ISTAX");                     // 是否含税（表头）
            e.FieldKeys.Add("AP_PAYABLEENTRY");           // 单据体
            e.FieldKeys.Add("FTAXPRICE");                 // 发票含税单价
            e.FieldKeys.Add("FPrice");                    // 发票单价（不含税）
            e.FieldKeys.Add("F_CustLi_PriceListTaxPrice"); // 价目表价格
        }


        /// <summary>
        /// 操作执行前（事务内、实际执行前）：添加非阻断的"不一致提醒"
        /// 说明：此时数据实体完整加载，避免 AfterExecute 中字段可能未加载的问题。
        /// </summary>
        public override void BeginOperationTransaction(BeginOperationTransactionArgs e)
        {
            base.BeginOperationTransaction(e);

            var errors = new List<string>();

            foreach (DynamicObject bill in e.DataEntitys)
            {
                // 仅财务应付单（立账类型 = 3）
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeFinance)
                {
                    continue;
                }

                bool includedTax = (bill["ISTAX"] != null) && Convert.ToBoolean(bill["ISTAX"]);

                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null)
                {
                    continue;
                }

                // 单据编号（用于提醒信息展示）
                string billNo = ObjectUtils.Object2String(
                    this.BusinessInfo.GetBillNoField().DynamicProperty.GetValueFast(bill));

                foreach (DynamicObject entry in entryObjs)
                {
                    decimal invoicePrice = 0m;
                    if (includedTax)
                    {
                        if (entry["TaxPrice"] != null && !(entry["TaxPrice"] is DBNull))
                            decimal.TryParse(entry["TaxPrice"].ToString(), out invoicePrice);
                    }
                    else
                    {
                        if (entry["FPrice"] != null && !(entry["FPrice"] is DBNull))
                            decimal.TryParse(entry["FPrice"].ToString(), out invoicePrice);
                    }

                    decimal listPrice = 0m;
                    if (entry["F_CustLi_PriceListTaxPrice"] != null && !(entry["F_CustLi_PriceListTaxPrice"] is DBNull))
                        decimal.TryParse(entry["F_CustLi_PriceListTaxPrice"].ToString(), out listPrice);

                    // 考虑尾差，绝对值小于容差视为一致
                    if (Math.Abs(invoicePrice - listPrice) > Tolerance)
                    {
                        errors.Add(string.Format(
                            "单据[{0}] 第 {1} 行 不一致，请确认是否需要更新采购价目表。",
                            billNo,
                            entry["SEQ"]));
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new KDBusinessException("", string.Join("\n", errors));
            }

            // 确保提醒信息在客户端弹出
            this.OperationResult.IsShowMessage = true;
        }
    }
}
