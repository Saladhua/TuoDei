using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;

using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-销售订单-按钮插件
    /// 功能：点击【匹配物料获取价格】按钮，根据表头客户/结算币别 + 明细物料/税率，
    /// 从历史已审核销售订单中批量取最新含税单价，回填到明细行的含税单价字段(FTAXPRICE)。
    /// 取价维度相对报价单更简化（不含国别范围、价格类型），仅匹配含税价。
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-销售订单-匹配物料获取价格")]
    public class LinLanXQSalOrderGetPricePlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 按钮点击事件处理：触发销售订单批量取价逻辑
        /// </summary>
        /// <param name="e">按钮事件参数，包含按钮标识Key</param>
        public override void EntryBarItemClick(BarItemClickEventArgs e)
        {
            base.EntryBarItemClick(e);

            // 只处理【匹配物料获取价格】按钮，按钮标识在BOS元数据中注册为 F_CustLI_MatchGetPriceO
            if (!e.BarItemKey.Equals("F_CustLI_MatchGetPriceO", StringComparison.OrdinalIgnoreCase)) return;

            DynamicObject billObj = this.View.Model.DataObject;
            if (billObj == null) return;

            // ---- 读取表头信息 ----
            // 销售订单取价只用到客户和结算币别，不需要国别范围和价格类型
            DynamicObject headObj = billObj;
            long customerId = 0;
            long settleCurrId = 0;

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

            // 获取明细行集合：SAL_ORDERENTRY 为销售订单的单据体标识
            DynamicObjectCollection entryCollection = billObj["SAL_ORDERENTRY"] as DynamicObjectCollection;
            if (entryCollection == null || entryCollection.Count == 0) return;

            // ---- 遍历明细构造取价请求列表 ----
            List<LinLanXQPriceQueryHelper.PriceRequest> requests = new List<LinLanXQPriceQueryHelper.PriceRequest>();

            foreach (DynamicObject entry in entryCollection)
            {
                long materialId = 0;
                string drawingNo = "";
                decimal taxRate = 0m;

                if (entry["FMATERIALID"] != null)
                {
                    DynamicObject matObj = entry["FMATERIALID"] as DynamicObject;
                    if (matObj != null)
                    {
                        materialId = Convert.ToInt64(matObj["Id"]);
                    }
                }

                if (entry["FTAXRATE"] != null)
                {
                    taxRate = Convert.ToDecimal(entry["FTAXRATE"]);
                }

                // 跳过未填物料的空行，避免无效取价请求
                if (materialId <= 0) continue;

                LinLanXQPriceQueryHelper.PriceRequest req = new LinLanXQPriceQueryHelper.PriceRequest
                {
                    CustomerId = customerId,
                    SettleCurrId = settleCurrId,
                    MaterialId = materialId,
                    DrawingNo = drawingNo,
                    TaxRate = taxRate,
                    IsForQuotation = false,  // 销售订单取价标记
                    PriceType = ""           // 销售订单不区分价格类型
                };
                requests.Add(req);
            }

            if (requests.Count == 0) return;

            // 调用公共帮助类批量取价
            List<LinLanXQPriceQueryHelper.PriceResult> results = LinLanXQPriceQueryHelper.BatchQueryPrices(this.View.Context, requests);

            // ---- 将取价结果回填到明细行 ----
            // 按行索引与请求顺序一一对应，只回填匹配成功的含税单价
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
                        // 调用 InvokeFieldUpdateService 触发金蝶字段值更新联动，刷新UI计算公式
                        this.View.InvokeFieldUpdateService("FTAXPRICE", index);
                    }
                }
                index++;
            }

            // 保存修改到数据库
            ((IBillView)this.View).Model.Save();
        }
    }
}
