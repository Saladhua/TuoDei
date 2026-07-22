using System;
using System.ComponentModel;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 益讯机械 - 财务应付单表单"取价目表价格"按钮插件
    /// 功能：点击 tbGetPriceForm 按钮时，将分录行价目表含税价写入对应单价字段
    /// </summary>
    [Description("益讯机械-财务应付单表单按钮获取价目表价格"), HotUpdate]
    public class YxjAPPayableGetPriceFormPlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 按钮点击事件处理
        /// </summary>
        /// <param name="e">按钮事件参数</param>
        public override void ButtonClick(ButtonClickEventArgs e)
        {
            base.ButtonClick(e);

            // 仅处理"取价目表价格"按钮
            if (e.Key != "tbGetPriceForm")
                return;

            // 读取单据头"含税"标志
            bool includedTax = false;
            var istaxObj = this.View.Model.GetValue("ISTAX");
            if (istaxObj != null)
            {
                includedTax = Convert.ToBoolean(istaxObj);
            }

            // 遍历分录行，逐行取价目表含税价并填充
            int rowCount = this.View.Model.GetEntryRowCount("FEntityDetail");
            for (int i = 0; i < rowCount; i++)
            {
                var priceListTaxPriceObj = this.View.Model.GetValue("F_CustLi_PriceListTaxPrice", i);
                // 此行无价目表价格，跳过
                if (priceListTaxPriceObj == null || priceListTaxPriceObj == DBNull.Value)
                    continue;

                decimal priceListTaxPrice = Convert.ToDecimal(priceListTaxPriceObj);

                // 含税 → 写入含税单价(FTAXPRICE)；不含税 → 写入单价(FPRICE)
                if (includedTax)
                {
                    var currentTaxPriceObj = this.View.Model.GetValue("FTAXPRICE", i);
                    if (currentTaxPriceObj != null && currentTaxPriceObj != DBNull.Value)
                    {
                        decimal currentTaxPrice = Convert.ToDecimal(currentTaxPriceObj);
                        // 与当前值相同则跳过，避免无谓更新
                        if (currentTaxPrice == priceListTaxPrice)
                            continue;
                    }

                    this.View.Model.SetValue("FTAXPRICE", priceListTaxPrice, i);
                    // 触发字段更新服务，联动计算价税合计等
                    this.View.InvokeFieldUpdateService("FTAXPRICE", i);
                }
                else
                {
                    var currentPriceObj = this.View.Model.GetValue("FPRICE", i);
                    if (currentPriceObj != null && currentPriceObj != DBNull.Value)
                    {
                        decimal currentPrice = Convert.ToDecimal(currentPriceObj);
                        // 与当前值相同则跳过，避免无谓更新
                        if (currentPrice == priceListTaxPrice)
                            continue;
                    }

                    this.View.Model.SetValue("FPRICE", priceListTaxPrice, i);
                    // 触发字段更新服务，联动计算价税合计等
                    this.View.InvokeFieldUpdateService("FPRICE", i);
                }
            }

            this.View.Model.Save();
        }
    }
}
