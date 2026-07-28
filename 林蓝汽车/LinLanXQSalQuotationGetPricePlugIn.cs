using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-销售报价单-按钮插件
    /// 功能：点击【匹配物料获取价格】按钮，根据表头客户/结算币别/国别范围/价格类型 + 明细物料/图号，
    /// 从历史销售订单中批量取价，将匹配到的含税单价(FTAXPRICE)和不含税单价(FPRICE)回填到明细行。
    /// 匹配不到价格时明细行留空不做赋值（此行为已由客户于2026-07-27确认）。
    /// 相比销售订单取价，报价单多了国别范围和价格类型两个取价维度，且同时回填含税价和不含税价。
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-销售报价单-匹配物料获取价格")]
    public class LinLanXQSalQuotationGetPricePlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 按钮点击事件处理：触发销售报价单批量取价逻辑
        /// </summary>
        /// <param name="e">按钮事件参数，包含按钮标识Key</param>
        public override void ButtonClick(ButtonClickEventArgs e)
        {
            base.ButtonClick(e);

            // 只处理【匹配物料获取价格】按钮，按钮标识在BOS元数据中注册为 QSGA_tbButton
            if (e.Key != "QSGA_tbButton") return;

            DynamicObject billObj = this.View.Model.DataObject;
            if (billObj == null) return;
            
            // ---- 读取表头信息 ----
            // 报价单取价维度比销售订单多：国别范围(F_CustLI_CountryRange1)和价格类型(F_CustLI_PriceType)
            DynamicObject headObj = billObj;
            long customerId = 0;
            long settleCurrId = 0;
            long countryRangeId = 0;
            string priceType = "";

            if (headObj["FCUSTID"] != null)
            {
                DynamicObject custObj = headObj["FCUSTID"] as DynamicObject;
                if (custObj != null)
                {
                    customerId = Convert.ToInt64(custObj["Id"]);
                }
            }

            if (headObj["FSettleCurrId"] != null)
            {
                DynamicObject currObj = headObj["FSettleCurrId"] as DynamicObject;
                if (currObj != null)
                {
                    settleCurrId = Convert.ToInt64(currObj["Id"]);
                }
            }

            // 注意：取价时若匹配不到价格，明细行价格留空不做赋值。
            // 此留空行为由客户明确确认，保留此注释作为日后Bug回溯依据。
            // （2026-07-27 客户确认：匹配不到价格时留空，系统会做校验）
            if (headObj["F_CustLI_CountryRange1"] != null)
            {
                DynamicObject countryObj = headObj["F_CustLI_CountryRange1"] as DynamicObject;
                if (countryObj != null)
                {
                    countryRangeId = Convert.ToInt64(countryObj["Id"]);
                }
            }

            if (headObj["F_CustLI_PriceType"] != null)
            {
                priceType = headObj["F_CustLI_PriceType"].ToString();
            }

            // 获取明细行集合：SAL_QUOTATIONENTRY 为销售报价单的单据体标识
            DynamicObjectCollection entryCollection = billObj["SAL_QUOTATIONENTRY"] as DynamicObjectCollection;
            if (entryCollection == null || entryCollection.Count == 0) return;

            // ---- 遍历明细构造取价请求列表 ----
            // 报价单取价传入图号(F_QSGA_Text_33z)作为辅助匹配条件，提高匹配精度
            List<LinLanXQPriceQueryHelper.PriceRequest> requests = new List<LinLanXQPriceQueryHelper.PriceRequest>();

            foreach (DynamicObject entry in entryCollection)
            {
                long materialId = 0;
                string drawingNo = "";

                if (entry["FMATERIALID"] != null)
                {
                    DynamicObject matObj = entry["FMATERIALID"] as DynamicObject;
                    if (matObj != null)
                    {
                        materialId = Convert.ToInt64(matObj["Id"]);
                    }
                }

                if (entry["F_QSGA_Text_33z"] != null)
                {
                    drawingNo = entry["F_QSGA_Text_33z"].ToString();
                }

                // 跳过未填物料的空行
                if (materialId <= 0) continue;

                LinLanXQPriceQueryHelper.PriceRequest req = new LinLanXQPriceQueryHelper.PriceRequest
                {
                    CustomerId = customerId,
                    SettleCurrId = settleCurrId,
                    CountryRangeId = countryRangeId,
                    MaterialId = materialId,
                    DrawingNo = drawingNo,
                    PriceType = priceType,
                    TaxRate = 0m,            // 报价单取价不匹配税率，由帮助类根据 IsForQuotation 跳过 FTAXRATE 条件
                    IsForQuotation = true     // 报价单取价标记
                };
                requests.Add(req);
            }

            if (requests.Count == 0) return;

            // 调用公共帮助类批量取价
            List<LinLanXQPriceQueryHelper.PriceResult> results = LinLanXQPriceQueryHelper.BatchQueryPrices(this.View.Context, requests);

            // ---- 将取价结果回填到明细行 ----
            // 报价单同时回填含税单价(FTAXPRICE)和不含税单价(FPRICE)
            int index = 0;
            foreach (DynamicObject entry in entryCollection)
            {
                long materialId = 0;
                if (entry["FMATERIALID"] != null)
                {
                    DynamicObject matObj = entry["FMATERIALID"] as DynamicObject;
                    if (matObj != null)
                    {
                        materialId = Convert.ToInt64(matObj["Id"]);
                    }
                }
                if (materialId <= 0) continue;

                if (index < results.Count)
                {
                    var result = results[index];
                    if (result.Success)
                    {
                        this.View.Model.SetValue("FTAXPRICE", result.TaxPrice, index);
                        this.View.Model.SetValue("FPRICE", result.Price, index);

                        // 调用 InvokeFieldUpdateService 触发含税价字段联动，刷新UI计算公式
                        // FPRICE 由系统根据含税价和税率自动计算，不需要单独触发更新
                        this.View.InvokeFieldUpdateService("FTAXPRICE", index);
                    }
                }
                index++;
            }
        }
    }
}
