using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-销售取价公共帮助类
    /// 根据客户、物料、结算币别等维度，从历史已审核销售订单中批量匹配最新价格。
    /// 适用于销售订单(取含税价)和销售报价单(取含税价+不含税价)两种场景。
    /// 性能约定：一次 IN 批量查询 + Dictionary 缓存，禁止在循环内逐单查 DB。
    /// </summary>
    public static class LinLanXQPriceQueryHelper
    {
        /// <summary>
        /// 取价请求维度：一组条件定义一个取价请求
        /// 销售订单和销售报价单使用相同的请求结构，通过 IsForQuotation 区分
        /// </summary>
        public class PriceRequest
        {
            public long CustomerId;       // 客户内码，匹配 T_SAL_ORDER.FCUSTID
            public long SettleCurrId;     // 结算币别内码，匹配 T_SAL_ORDER.FSETTLECURRID
            public long MaterialId;       // 物料内码，匹配 T_SAL_ORDERENTRY.FMATERIALID
            public string DrawingNo;      // 产品图号（销售报价单使用），匹配 T_SAL_ORDERENTRY.F_QSGA_TEXT_33Z
            public decimal TaxRate;       // 税率，匹配 T_SAL_ORDERENTRY.FTAXRATE
            public string CountryRangeId;   // 国别范围（销售报价单使用），辅助报价单取价过滤
            public string PriceType;      // 价格类型（销售报价单使用），辅助报价单取价过滤
            public bool IsForQuotation;   // true=报价单取价, false=销售订单取价
        }

        /// <summary>
        /// 取价结果：包含含税单价、不含税单价及来源单据信息
        /// 由 BatchQueryPrices 填充并返回
        /// </summary>
        public class PriceResult
        {
            public decimal TaxPrice;        // 含税单价（对应 FTAXPRICE）
            public decimal Price;           // 不含税单价（对应 FPRICE）
            public long SettleCurrId;       // 结算币别内码
            public string SourceBillNo;     // 来源销售订单编号（供日志追溯）
            public bool Success;            // true=匹配成功, false=未匹配到价格
            public string Message;          // 成功时为空，失败时包含失败原因描述
        }

        /// <summary>
        /// 批量取价：从已审核销售订单中按(客户, 结算币别, 物料, 税率)维度匹配最新价格
        /// 查询结果按单据日期降序排列，每组条件只取第一条（即最新的一笔）
        /// </summary>
        /// <param name="ctx">金蝶上下文对象</param>
        /// <param name="requests">取价请求列表，对应单据明细行</param>
        /// <returns>与请求顺序一一对应的取价结果列表</returns>
        public static List<PriceResult> BatchQueryPrices(Context ctx, List<PriceRequest> requests)
        {
            // resultDict 按请求 key 缓存最终结果，保证每个请求都有对应的 PriceResult
            Dictionary<string, PriceResult> resultDict = new Dictionary<string, PriceResult>();
            if (requests == null || requests.Count == 0)
                return new List<PriceResult>();

            // ---- 构造批量查询 SQL ----
            // 所有请求的公共条件相同（表头：客户/结算币别/国别范围/价格类型），只有物料不同
            // 公共条件提到 WHERE 层，物料用 IN 列表，避免 N 个 OR 重复相同条件
            var firstReq = requests[0];

            StringBuilder sql = new StringBuilder();
            sql.AppendLine("SELECT a.FID, e.FENTRYID, e.FMATERIALID,");
            sql.AppendLine("       f.FTAXPRICE, f.FPRICE, f.FTAXRATE,");
            sql.AppendLine("       fin.FSETTLECURRID, a.FBILLNO, a.FDATE, e.FSEQ");
            sql.AppendLine("FROM T_SAL_ORDER a");
            sql.AppendLine("JOIN T_SAL_ORDERENTRY e ON a.FID = e.FID");
            sql.AppendLine("JOIN T_SAL_ORDERENTRY_F f ON e.FENTRYID = f.FENTRYID");
            sql.AppendLine("JOIN T_SAL_ORDERFIN fin ON a.FID = fin.FID");
            sql.AppendLine("LEFT JOIN T_BD_CUSTOMER c ON a.FCUSTID = c.FCUSTID");
            sql.AppendLine("WHERE a.FDOCUMENTSTATUS = 'C'");

            // 客户：有值才过滤
            if (firstReq.CustomerId != 0)
                sql.AppendLine("AND a.FCUSTID = " + firstReq.CustomerId.ToString());

            // 结算币别：有值才过滤
            if (firstReq.SettleCurrId != 0)
                sql.AppendLine("AND fin.FSETTLECURRID = " + firstReq.SettleCurrId.ToString());

            // 国别范围：有值才过滤（取客户基础资料上的 FCOUNTRY 字段）
            if (!string.IsNullOrEmpty(firstReq.CountryRangeId))
                sql.AppendLine("AND c.FCOUNTRY = '" + firstReq.CountryRangeId.Replace("'", "''") + "'");

            // 图号：非空（去除空格）才过滤
            if (!string.IsNullOrWhiteSpace(firstReq.DrawingNo))
                sql.AppendLine("AND e.F_QSGA_TEXT_33Z = '" + firstReq.DrawingNo.Replace("'", "''") + "'");

            // 税率：仅销售订单取价需要，报价单取价不匹配税率
            if (!firstReq.IsForQuotation)
                sql.AppendLine("AND f.FTAXRATE = " + firstReq.TaxRate.ToString("F6"));

            // 物料 IN 列表
            string[] matIds = new string[requests.Count];
            for (int i = 0; i < requests.Count; i++)
                matIds[i] = requests[i].MaterialId.ToString();
            sql.AppendLine("AND e.FMATERIALID IN (" + string.Join(",", matIds) + ")");

            // 根据价格类型动态调整排序，保证每组条件的第一条记录即为目标价格
            // PriceType: "1"=最新价格, "2"=最低价格, "3"=最高价格; ""=销售订单取最新价
            string priceType = requests.Count > 0 ? requests[0].PriceType : "";
            switch (priceType)
            {
                case "2":
                    sql.AppendLine("ORDER BY f.FTAXPRICE ASC, a.FDATE DESC");
                    break;
                case "3":
                    sql.AppendLine("ORDER BY f.FTAXPRICE DESC, a.FDATE DESC");
                    break;
                default:
                    sql.AppendLine("ORDER BY a.FApproveDate DESC, a.FBILLNO DESC");
                    break;
            }

            DataSet ds = null;
            try
            {
                ds = DBServiceHelper.ExecuteDataSet(ctx, sql.ToString());
            }
            catch (Exception ex)
            {
                // 查询异常时（如DB超时、语法错误），所有请求标记为失败并携带异常信息
                foreach (var req in requests)
                {
                    string key = BuildKey(req);
                    resultDict[key] = new PriceResult
                    {
                        Success = false,
                        Message = "查询历史销售订单异常: " + ex.Message
                    };
                }
                return ConvertDictToList(resultDict, requests);
            }

            // ---- 二阶段结果处理 ----
            // 阶段1：tempResults 临时缓存，按 key 去重，每组条件只保留第一条（最新）
            // 阶段2：resultDict 确保每个请求都有对应的 PriceResult，无匹配的填充失败信息
            Dictionary<string, PriceResult> tempResults = new Dictionary<string, PriceResult>();

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    long materialId = Convert.ToInt64(row["FMATERIALID"]);
                    long settleCurrId = Convert.ToInt64(row["FSETTLECURRID"]);
                    string billNo = row["FBILLNO"].ToString();
                    decimal taxPrice = row["FTAXPRICE"] == DBNull.Value ? 0m : Convert.ToDecimal(row["FTAXPRICE"]);
                    decimal price = row["FPRICE"] == DBNull.Value ? 0m : Convert.ToDecimal(row["FPRICE"]);

                    // 将每条数据库记录与所有请求进行匹配
                    // 由于结果已按日期倒序，第一个匹配到的就是最新的价格
                    foreach (var req in requests)
                    {
                        if (req.MaterialId != materialId) continue;
                        if (req.SettleCurrId != settleCurrId) continue;

                        string key = BuildKey(req);
                        // 若该请求已被匹配过，跳过后续重复记录
                        if (tempResults.ContainsKey(key)) continue;

                        tempResults[key] = new PriceResult
                        {
                            TaxPrice = taxPrice,
                            Price = price,
                            SettleCurrId = settleCurrId,
                            SourceBillNo = billNo,
                            Success = true,
                            Message = ""
                        };
                    }
                }
            }

            // 将临时结果转移到最终结果，未匹配到的请求填充为失败状态
            foreach (var req in requests)
            {
                string key = BuildKey(req);
                if (tempResults.ContainsKey(key))
                {
                    resultDict[key] = tempResults[key];
                }
                else
                {
                    resultDict[key] = new PriceResult
                    {
                        Success = false,
                        Message = "未在历史销售订单中匹配到价格"
                    };
                }
            }

            return ConvertDictToList(resultDict, requests);
        }

        private static string BuildKey(PriceRequest req)
        {
            return req.MaterialId.ToString() + "_" + req.SettleCurrId.ToString() + "_" + req.CountryRangeId;
        }

        /// <summary>
        /// 将字典结果按请求顺序转换为列表返回
        /// 保证返回结果顺序与传入的 requests 顺序一一对应，便于调用方按行索引回填
        /// </summary>
        private static List<PriceResult> ConvertDictToList(Dictionary<string, PriceResult> dict, List<PriceRequest> requests)
        {
            List<PriceResult> list = new List<PriceResult>();
            foreach (var req in requests)
            {
                string key = BuildKey(req);
                if (dict.ContainsKey(key))
                {
                    list.Add(dict[key]);
                }
                else
                {
                    // 理论上不会进入此分支（BatchQueryPrices 已保证每个请求都有结果）
                    // 保留此防御代码防止重构遗漏
                    list.Add(new PriceResult
                    {
                        Success = false,
                        Message = "未在历史销售订单中匹配到价格"
                    });
                }
            }
            return list;
        }
    }
}
