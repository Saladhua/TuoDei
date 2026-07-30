using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill;
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
    /// 功能：点击【匹配物料获取价格】按钮，先通过图号(F_QSGA_Text_33z)将物料为空的明细行
    /// 从物料主表(T_BD_MATERIAL)匹配物料回填，再根据表头客户/结算币别/国别范围/价格类型 +
    /// 明细物料/图号，从历史销售订单中批量取价，将匹配到的含税单价(FTAXPRICE)和不含税单价(FPRICE)
    /// 回填到明细行。匹配不到价格时明细行留空不做赋值（此行为已由客户于2026-07-27确认）。
    /// 取价维度相比销售订单多了国别范围(F_CustLI_CountryRange1)和价格类型(F_CustLI_PriceType)，
    /// 且同时回填含税价和不含税价。
    ///
    /// 逻辑结构：
    ///   Loop 1 — 一次遍历明细行，物料已有的直接构造取价请求，物料为空的有图号则收集图号
    ///   批量查物料 — 从 T_BD_MATERIAL 按图号匹配物料内码
    ///   追加请求 — 图号匹配到的追加取价请求
    ///   批量取价 — 调用 LinLanXQPriceQueryHelper.BatchQueryPrices
    ///   Loop 2 — 按请求行索引(requestRowIndices)统一回填物料和价格
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-销售报价单-匹配物料获取价格")]
    public class LinLanXQSalQuotationGetPricePlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 按钮点击事件处理：触发销售报价单批量取价逻辑。
        /// 涉及字段：表头(CUSTID, SettleCurrId, F_CustLI_CountryRange1, F_CustLI_PriceType)，
        /// 明细(MATERIALID, F_QSGA_Text_33z)。
        /// 回填字段：FMATERIALID（图号解析的行）、FTAXPRICE、FPRICE。
        /// </summary>
        /// <param name="e">按钮事件参数，包含按钮标识Key（QSGA_tbButton）</param>
        public override void EntryBarItemClick(BarItemClickEventArgs e)
        {
            base.EntryBarItemClick(e);

            // 只处理【匹配物料获取价格】按钮，按钮标识在BOS元数据中注册为 QSGA_tbButton
            if (!e.BarItemKey.Equals("QSGA_tbButton", StringComparison.OrdinalIgnoreCase)) return;

            DynamicObject billObj = this.View.Model.DataObject;
            if (billObj == null) return;
            
            // ---- 读取表头信息 ----
            // 报价单取价维度比销售订单多：国别范围(F_CustLI_CountryRange1)和价格类型(F_CustLI_PriceType)
            DynamicObject headObj = billObj;
            long customerId = 0;
            long settleCurrId = 0;
            string countryRangeId = "";
            string priceType = "";

            if (headObj["CUSTID"] != null)
            {
                DynamicObject custObj = headObj["CUSTID"] as DynamicObject;
                if (custObj != null)
                {
                    customerId = Convert.ToInt64(custObj["Id"]);
                }
            }

            if (billObj["SAL_QUOTATIONFIN"] != null)
            {
                DynamicObjectCollection finCollection = billObj["SAL_QUOTATIONFIN"] as DynamicObjectCollection;
                if (finCollection != null && finCollection.Count > 0)
                {
                    DynamicObject finObj = finCollection[0];
                    if (finObj["SettleCurrId"] != null)
                    {
                        DynamicObject currObj = finObj["SettleCurrId"] as DynamicObject;
                        if (currObj != null)
                        {
                            long.TryParse(currObj["Id"]?.ToString(), out long parsedId);
                            settleCurrId = parsedId;
                        }
                    }
                }
            }

            // 注意：取价时若匹配不到价格，明细行价格留空不做赋值。
            // 此留空行为由客户明确确认，保留此注释作为日后Bug回溯依据。
            // （2026-07-27 客户确认：匹配不到价格时留空，系统会做校验）
            if (headObj["F_CustLI_CountryRange1"] != null)
            {
                DynamicObject countryObj = headObj["F_CustLI_CountryRange1"] as DynamicObject;
                if (countryObj != null && countryObj["Id"] != null)
                {
                    countryRangeId = countryObj["Id"].ToString();
                }
            }

            if (headObj["F_CustLI_PriceType"] != null)
            {
                priceType = headObj["F_CustLI_PriceType"].ToString();
            }

            // 获取明细行集合：SAL_QUOTATIONENTRY 为销售报价单的单据体标识
            DynamicObjectCollection entryCollection = billObj["SAL_QUOTATIONENTRY"] as DynamicObjectCollection;
            if (entryCollection == null || entryCollection.Count == 0) return;

            // ── Loop 1: 一次遍历明细行，分流处理 ──
            // branch A: 已有物料 → 直接构造取价请求，记录行号
            // branch B: 物料为空但有图号 → 收集图号，后续批量匹配后追加请求
            // requestRowIndices：与 requests 一一对应，记录每笔请求对应的明细行号，供 Loop 2 回填使用
            // resolvedMaterialMap：缓存图号解析到的物料ID（key=行号, value=物料内码）
            Dictionary<int, long> resolvedMaterialMap = new Dictionary<int, long>();
            List<int> requestRowIndices = new List<int>();
            List<LinLanXQPriceQueryHelper.PriceRequest> requests = new List<LinLanXQPriceQueryHelper.PriceRequest>();
            List<string> drawingNoList = new List<string>();
            Dictionary<int, string> rowDrawingMap = new Dictionary<int, string>();

            for (int i = 0; i < entryCollection.Count; i++)
            {
                DynamicObject entry = entryCollection[i];

                // 统一读取物料ID（标准字段 DynamicObject 中去 F）
                long materialId = 0;
                if (entry["MATERIALID"] != null)
                {
                    DynamicObject matObj = entry["MATERIALID"] as DynamicObject;
                    if (matObj != null) materialId = Convert.ToInt64(matObj["Id"]);
                }

                if (materialId > 0)
                {
                    // branch A: 已有物料的明细行 → 直接按现有取价条件构造请求（含图号辅助匹配）
                    requests.Add(new LinLanXQPriceQueryHelper.PriceRequest
                    {
                        CustomerId = customerId,
                        SettleCurrId = settleCurrId,
                        CountryRangeId = countryRangeId,
                        MaterialId = materialId,
                        DrawingNo = entry["F_QSGA_Text_33z"]?.ToString() ?? "",
                        PriceType = priceType,
                        TaxRate = 0m,
                        IsForQuotation = true
                    });
                    requestRowIndices.Add(i);
                    // 已有物料已完成请求构造，跳过后续图号收集逻辑
                    continue;
                }

                // branch B: 物料为空但有图号 → 收集图号，待批量从物料主表匹配
                string drawingNo = entry["F_QSGA_Text_33z"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(drawingNo))
                {
                    drawingNoList.Add(drawingNo);
                    rowDrawingMap[i] = drawingNo;
                }
            }

            // 批量查物料：从 T_BD_MATERIAL 按产品图号(F_QSGA_TEXT_33Z)一次 IN 查询匹配物料内码
            // 将匹配结果缓存到 resolvedMaterialMap（key=行号, value=物料内码）
            if (drawingNoList.Count > 0)
            {
                var materialMap = LinLanXQMaterialHelper.BatchQueryMaterialByDrawingNos(this.View.Context, drawingNoList);
                foreach (var kvp in rowDrawingMap)
                {
                    if (materialMap.ContainsKey(kvp.Value))
                        resolvedMaterialMap[kvp.Key] = materialMap[kvp.Value];
                }
                // 图号匹配到物料的追加取价请求，物料来源为解析结果
                foreach (var kvp in resolvedMaterialMap)
                {
                    requests.Add(new LinLanXQPriceQueryHelper.PriceRequest
                    {
                        CustomerId = customerId,
                        SettleCurrId = settleCurrId,
                        CountryRangeId = countryRangeId,
                        MaterialId = kvp.Value,
                        DrawingNo = "",
                        PriceType = priceType,
                        TaxRate = 0m,
                        IsForQuotation = true
                    });
                    requestRowIndices.Add(kvp.Key);
                }
            }

            // 所有行均无物料且图号未匹配到 → 无请求可执行，直接返回
            if (requests.Count == 0) return;

            // 批量取价：从历史已审核销售订单中按客户/结算币别/国别/价格类型+物料/图号匹配价格
            List<LinLanXQPriceQueryHelper.PriceResult> results = LinLanXQPriceQueryHelper.BatchQueryPrices(this.View.Context, requests);

            // ── Loop 2: 按行顺序统一回填（物料+价格+联动） ──
            // SetValue 后立即 InvokeFieldUpdateService 确保界面值更新联动
            for (int idx = 0; idx < requestRowIndices.Count; idx++)
            {
                int row = requestRowIndices[idx];

                // 仅图号解析出物料的需 SetValue 回填并触发联动刷新界面
                if (resolvedMaterialMap.ContainsKey(row))
                {
                    this.View.Model.SetValue("FMATERIALID", resolvedMaterialMap[row], row);
                    this.View.InvokeFieldUpdateService("FMATERIALID", row);
                }

                // 价格匹配成功才 SetValue，不成功留空（客户 2026-07-27 确认）
                if (idx < results.Count && results[idx].Success)
                {
                    this.View.Model.SetValue("FTAXPRICE", results[idx].TaxPrice, row);
                    this.View.Model.SetValue("FPRICE", results[idx].Price, row);
                    // FPRICE 由系统根据含税价和税率自动计算，不需要单独触发更新
                    this.View.InvokeFieldUpdateService("FTAXPRICE", row);
                }
            }

            // 一次性保存所有修改（物料赋值 + 价格赋值）
            ((IBillView)this.View).Model.Save();
        }
    }
}
