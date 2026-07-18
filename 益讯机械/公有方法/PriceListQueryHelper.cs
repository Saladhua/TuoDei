using System;
using System.Collections.Generic;
using System.Data;
using Kingdee.BOS;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 采购价目表取价公共帮助类（益讯机械 - 应付单取价及校验）
    /// 负责按 "供应商 + 物料 + 价格类型(标准采购/委外) + 是否含税" 维度，
    /// 从采购价目表(PUR_PriceCategory)批量取 "生效日期最新" 的含税单价(FTAXPRICE)。
    /// 性能约定：一次 IN 批量查询 + Dictionary 缓存，禁止在循环内逐单查 DB。
    /// </summary>
    public static class PriceListQueryHelper
    {
        #region 取价请求维度

        /// <summary>
        /// 取价请求维度：一个元组即一组取价条件
        /// </summary>
        public class PriceReq
        {
            public long SupplierId;   // 供应商内码
            public long MaterialId;   // 物料内码
            public int PriceType;     // 价格类型：2 = 标准采购，3 = 委外
            public bool IncludedTax;  // 是否含税（对应价目表字段 FIsIncludedTax）
        }

        /// <summary>
        /// 取价结果：同时包含含税价和不含税价，由调用方决定取哪个
        /// </summary>
        public class PriceBothResult
        {
            public decimal? TaxPrice { get; set; }  // 含税单价 (FTAXPRICE)
            public decimal? Price { get; set; }      // 单价 (FPRICE)
        }

        #endregion

        #region 对外方法

        /// <summary>
        /// 批量取价：按 (供应商, 物料, 价格类型, 是否含税) 维度，
        /// 取每组"生效日期(FEFFECTIVEDATE)最新"的含税单价。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="reqs">取价请求列表</param>
        /// <returns>维度key -> 含税单价（取不到返回空）</returns>
        public static Dictionary<string, decimal?> GetLatestTaxPrice(Context ctx, List<PriceReq> reqs)
        {
            var result = new Dictionary<string, decimal?>();

            // 空请求直接返回，避免无谓查询
            if (reqs == null || reqs.Count == 0)
            {
                return result;
            }

            // 1. 维度去重，构造 IN 条件（一次查询覆盖全部请求）
            var suppliers = new HashSet<long>();
            var materials = new HashSet<long>();
            var priceTypes = new HashSet<int>();
            var taxFlags = new HashSet<int>(); // 0/1

            foreach (var r in reqs)
            {
                suppliers.Add(r.SupplierId);
                materials.Add(r.MaterialId);
                priceTypes.Add(r.PriceType);
                taxFlags.Add(r.IncludedTax ? 1 : 0);
            }

            // 2. 拼接 SQL：物理表 t_PUR_PriceList(头) + t_PUR_PriceListEntry(体)
            //    过滤：供应商 / 物料 / 价格类型 / 是否含税 / 未禁用(FDISABLESTATUS<>'A')
            //    按生效日期降序，保证同维度第一条即"最新"
             string sql = string.Format(@"
                SELECT a.FMATERIALID     AS FMATERIALID,
                       b.FSUPPLIERID     AS FSUPPLIERID,
                       b.FPriceType      AS FPRICETYPE,
                       b.FIsIncludedTax  AS FISINCLUDEDTAX,
                       a.FTAXPRICE       AS FTAXPRICE,
                       a.FPRICE          AS FPRICE,
                       a.FEFFECTIVEDATE  AS FEFFECTIVEDATE
                FROM t_PUR_PriceListEntry a
                INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                WHERE a.FMATERIALID   IN ({0})
                  AND b.FSUPPLIERID   IN ({1})
                  AND b.FPriceType    IN ({2})
                  AND b.FIsIncludedTax IN ({3})
                  AND a.FDISABLESTATUS <> 'A'
                ORDER BY a.FEFFECTIVEDATE DESC
                ",
                string.Join(",", materials),
                string.Join(",", suppliers),
                string.Join(",", priceTypes),
                string.Join(",", taxFlags));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return result;
            }

            // 3. 组装结果：按维度key去重，因已按生效日期降序，首个即最新
            //    含税(tax==1)取FTAXPRICE，不含税(tax==0)取FPRICE
            foreach (DataRow row in ds.Tables[0].Rows)
            {
                long mat = Convert.ToInt64(row["FMATERIALID"]);
                long sup = Convert.ToInt64(row["FSUPPLIERID"]);
                int pt = Convert.ToInt32(row["FPRICETYPE"]);
                int tax = Convert.ToInt32(row["FISINCLUDEDTAX"]);
                decimal price;
                if (tax == 1)
                    price = (row["FTAXPRICE"] == DBNull.Value) ? 0m : Convert.ToDecimal(row["FTAXPRICE"]);
                else
                    price = (row["FPRICE"] == DBNull.Value) ? 0m : Convert.ToDecimal(row["FPRICE"]);

                string key = BuildKey(sup, mat, pt, tax);
                if (!result.ContainsKey(key))
                {
                    result[key] = price;
                }
            }

            return result;
        }

        /// <summary>
        /// 根据 源单类型(FSOURCETYPE) + 源单编号(FSourceBillNo) 推导价格类型：
        /// - FSOURCETYPE == 'STK_InStock' 且存在源单编号：查采购入库单(STK_InStock)的
        ///   业务类型(FBusinessType)，CG=采购(2)，WW=委外(3)
        /// - 其它来源（手动创建 / 采购订单下推 / 收料通知单下推等）：默认标准采购(2)
        /// </summary>
        /// <param name="sourceType">源单类型</param>
        /// <param name="sourceBillNo">源单编号</param>
        /// <param name="bizTypeMap">源单编号 -> 业务类型 的批量查询结果</param>
        public static int DerivePriceType(string sourceType, string sourceBillNo, Dictionary<string, string> bizTypeMap)
        {
            if (sourceType == "STK_InStock" && !string.IsNullOrEmpty(sourceBillNo))
            {
                if (bizTypeMap != null && bizTypeMap.TryGetValue(sourceBillNo, out string bt))
                {
                    // WW 表示委外，其余（含 CG）均按标准采购处理
                    if (bt == "WW")
                    {
                        return 3;
                    }
                    return 2;
                }
            }

            // 无来源或非采购入库单来源，默认标准采购
            return 2;
        }

        /// <summary>
        /// 批量查询采购入库单(STK_InStock)的 业务类型(FBusinessType)。
        /// 按源单编号 IN 一次查出，返回 源单编号 -> 业务类型(CG/WW/...) 的映射。
        /// </summary>
        public static Dictionary<string, string> GetBusinessTypeMap(Context ctx, List<string> billNos)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (billNos == null || billNos.Count == 0)
            {
                return map;
            }

            // 去重 + 转义单引号，防止 SQL 注入
            var distinct = new List<string>();
            foreach (var n in billNos)
            {
                if (!string.IsNullOrEmpty(n) && !distinct.Contains(n))
                {
                    distinct.Add(n.Replace("'", "''"));
                }
            }

            if (distinct.Count == 0)
            {
                return map;
            }

            string sql = string.Format(
                @"/*dialect*/ SELECT FBILLNO, FBusinessType FROM T_STK_INSTOCK WHERE FBILLNO IN ('{0}')",
                string.Join("','", distinct));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string no = (row["FBILLNO"] == DBNull.Value) ? string.Empty : row["FBILLNO"].ToString();
                    string bt = (row["FBusinessType"] == DBNull.Value) ? string.Empty : row["FBusinessType"].ToString();
                    if (!string.IsNullOrEmpty(no))
                    {
                        map[no] = bt;
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// 追溯暂估应付单的源单信息：财务应付单的 FSourceBillNo 指向暂估应付单，
        /// 需要查暂估应付单本身的源单（通常是采购入库单）来推导价格类型。
        /// 返回 key = "暂估单号_物料内码" → [FSOURCETYPE, FSourceBillNo]
        /// </summary>
        public static Dictionary<string, string[]> GetTempPayableSourceMap(Context ctx, List<string> tempBillNos)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (tempBillNos == null || tempBillNos.Count == 0) return map;

            var distinct = new List<string>();
            foreach (var n in tempBillNos)
            {
                if (!string.IsNullOrEmpty(n) && !distinct.Contains(n))
                    distinct.Add(n.Replace("'", "''"));
            }
            if (distinct.Count == 0) return map;

            string sql = string.Format(
                @"/*dialect*/ SELECT b.FBILLNO AS FBillNo, e.FMATERIALID, e.FSOURCETYPE, e.FSourceBillNo
                  FROM T_AP_PAYABLEENTRY e
                  INNER JOIN T_AP_PAYABLE b ON e.FID = b.FID
                  WHERE b.FBILLNO IN ('{0}')
                    AND b.FSETACCOUNTTYPE = 2
                    AND e.FSOURCETYPE IS NOT NULL
                    AND e.FSOURCETYPE <> ''
                    AND e.FSourceBillNo IS NOT NULL
                    AND e.FSourceBillNo <> ''",
                string.Join("','", distinct));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string billNo = (row["FBillNo"] == DBNull.Value) ? "" : row["FBillNo"].ToString();
                    long matId = (row["FMATERIALID"] == DBNull.Value) ? 0L : Convert.ToInt64(row["FMATERIALID"]);
                    string srcType = (row["FSOURCETYPE"] == DBNull.Value) ? "" : row["FSOURCETYPE"].ToString();
                    string srcBillNo = (row["FSourceBillNo"] == DBNull.Value) ? "" : row["FSourceBillNo"].ToString();

                    if (!string.IsNullOrEmpty(billNo) && !string.IsNullOrEmpty(srcType) && !string.IsNullOrEmpty(srcBillNo))
                    {
                        string key = string.Format("{0}_{1}", billNo, matId);
                        if (!map.ContainsKey(key))
                        {
                            map[key] = new[] { srcType, srcBillNo };
                        }
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// 按供应商 + 物料 + 是否含税 维度取最新含税单价（不区分价格类型）。
        /// 当无法推导价格类型时（如没有源单信息），用此方法兜底。
        /// </summary>
        public static Dictionary<string, decimal?> GetLatestTaxPriceAnyType(Context ctx, List<PriceReq> reqs)
        {
            var result = new Dictionary<string, decimal?>();
            if (reqs == null || reqs.Count == 0) return result;

            var suppliers = new HashSet<long>();
            var materials = new HashSet<long>();
            var taxFlags = new HashSet<int>();

            foreach (var r in reqs)
            {
                suppliers.Add(r.SupplierId);
                materials.Add(r.MaterialId);
                taxFlags.Add(r.IncludedTax ? 1 : 0);
            }

            string sql = string.Format(@"
                SELECT a.FMATERIALID     AS FMATERIALID,
                       b.FSUPPLIERID     AS FSUPPLIERID,
                       b.FIsIncludedTax  AS FISINCLUDEDTAX,
                       a.FTAXPRICE       AS FTAXPRICE,
                       a.FPRICE          AS FPRICE,
                       a.FEFFECTIVEDATE  AS FEFFECTIVEDATE
                FROM t_PUR_PriceListEntry a
                INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                WHERE a.FMATERIALID    IN ({0})
                  AND b.FSUPPLIERID    IN ({1})
                  AND b.FIsIncludedTax IN ({2})
                  AND a.FDISABLESTATUS <> 'A'
                ORDER BY a.FEFFECTIVEDATE DESC
                ",
                string.Join(",", materials),
                string.Join(",", suppliers),
                string.Join(",", taxFlags));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            foreach (DataRow row in ds.Tables[0].Rows)
            {
                long mat = Convert.ToInt64(row["FMATERIALID"]);
                long sup = Convert.ToInt64(row["FSUPPLIERID"]);
                int tax = Convert.ToInt32(row["FISINCLUDEDTAX"]);
                decimal price;
                if (tax == 1)
                    price = (row["FTAXPRICE"] == DBNull.Value) ? 0m : Convert.ToDecimal(row["FTAXPRICE"]);
                else
                    price = (row["FPRICE"] == DBNull.Value) ? 0m : Convert.ToDecimal(row["FPRICE"]);

                string key = BuildKeyNoType(sup, mat, tax);
                if (!result.ContainsKey(key))
                {
                    result[key] = price;
                }
            }

            return result;
        }

        #endregion

        #region 不区分是否含税的取价方法

        /// <summary>
        /// 按 (供应商,物料,价格类型) 维度取最新价目表记录，不区分是否含税。
        /// 返回含税价(FTAXPRICE)和不含税价(FPRICE)，由调用方根据单据的 ISTAX 决定取哪个。
        /// </summary>
        public static Dictionary<string, PriceBothResult> GetLatestPriceEntryWithType(Context ctx, List<PriceReq> reqs)
        {
            var result = new Dictionary<string, PriceBothResult>();
            if (reqs == null || reqs.Count == 0) return result;

            var suppliers = new HashSet<long>();
            var materials = new HashSet<long>();
            var priceTypes = new HashSet<int>();

            foreach (var r in reqs)
            {
                suppliers.Add(r.SupplierId);
                materials.Add(r.MaterialId);
                priceTypes.Add(r.PriceType);
            }

            string sql = string.Format(@"
                SELECT a.FMATERIALID     AS FMATERIALID,
                       b.FSUPPLIERID     AS FSUPPLIERID,
                       b.FPriceType      AS FPRICETYPE,
                       a.FTAXPRICE       AS FTAXPRICE,
                       a.FPRICE          AS FPRICE,
                       a.FEFFECTIVEDATE  AS FEFFECTIVEDATE
                FROM t_PUR_PriceListEntry a
                INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                WHERE a.FMATERIALID    IN ({0})
                  AND b.FSUPPLIERID    IN ({1})
                  AND b.FPriceType     IN ({2})
                  AND a.FDISABLESTATUS <> 'A'
                ORDER BY a.FEFFECTIVEDATE DESC
                ",
                string.Join(",", materials),
                string.Join(",", suppliers),
                string.Join(",", priceTypes));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            foreach (DataRow row in ds.Tables[0].Rows)
            {
                long mat = Convert.ToInt64(row["FMATERIALID"]);
                long sup = Convert.ToInt64(row["FSUPPLIERID"]);
                int pt = Convert.ToInt32(row["FPRICETYPE"]);
                decimal? taxPrice = (row["FTAXPRICE"] == DBNull.Value) ? null : (decimal?)Convert.ToDecimal(row["FTAXPRICE"]);
                decimal? price = (row["FPRICE"] == DBNull.Value) ? null : (decimal?)Convert.ToDecimal(row["FPRICE"]);

                string key = BuildKeyNoTax(sup, mat, pt);
                if (!result.ContainsKey(key))
                {
                    result[key] = new PriceBothResult { TaxPrice = taxPrice, Price = price };
                }
            }

            return result;
        }

        /// <summary>
        /// 按 (供应商,物料) 维度取最新价目表记录，不区分价格类型和是否含税。
        /// 无源单信息时的兜底取价。
        /// </summary>
        public static Dictionary<string, PriceBothResult> GetLatestPriceEntryAnyType(Context ctx, List<PriceReq> reqs)
        {
            var result = new Dictionary<string, PriceBothResult>();
            if (reqs == null || reqs.Count == 0) return result;

            var suppliers = new HashSet<long>();
            var materials = new HashSet<long>();

            foreach (var r in reqs)
            {
                suppliers.Add(r.SupplierId);
                materials.Add(r.MaterialId);
            }

            string sql = string.Format(@"
                SELECT a.FMATERIALID     AS FMATERIALID,
                       b.FSUPPLIERID     AS FSUPPLIERID,
                       a.FTAXPRICE       AS FTAXPRICE,
                       a.FPRICE          AS FPRICE,
                       a.FEFFECTIVEDATE  AS FEFFECTIVEDATE
                FROM t_PUR_PriceListEntry a
                INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                WHERE a.FMATERIALID    IN ({0})
                  AND b.FSUPPLIERID    IN ({1})
                  AND a.FDISABLESTATUS <> 'A'
                ORDER BY a.FEFFECTIVEDATE DESC
                ",
                string.Join(",", materials),
                string.Join(",", suppliers));

            DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            foreach (DataRow row in ds.Tables[0].Rows)
            {
                long mat = Convert.ToInt64(row["FMATERIALID"]);
                long sup = Convert.ToInt64(row["FSUPPLIERID"]);
                decimal? taxPrice = (row["FTAXPRICE"] == DBNull.Value) ? null : (decimal?)Convert.ToDecimal(row["FTAXPRICE"]);
                decimal? price = (row["FPRICE"] == DBNull.Value) ? null : (decimal?)Convert.ToDecimal(row["FPRICE"]);

                string key = BuildKeyNoTypeNoTax(sup, mat);
                if (!result.ContainsKey(key))
                {
                    result[key] = new PriceBothResult { TaxPrice = taxPrice, Price = price };
                }
            }

            return result;
        }

        #endregion

        #region 内部工具

        /// <summary>
        /// 由取价维度构造字典 key（与 SQL 查询返回的维度保持一致）
        /// </summary>
        public static string BuildKey(PriceReq req)
        {
            return BuildKey(req.SupplierId, req.MaterialId, req.PriceType, req.IncludedTax ? 1 : 0);
        }

        /// <summary>
        /// 由维度分量构造字典 key
        /// </summary>
        private static string BuildKey(long sup, long mat, int pt, int tax)
        {
            return string.Format("{0}_{1}_{2}_{3}", sup, mat, pt, tax);
        }

        /// <summary>
        /// 不区分价格类型的 key：sup_mat_tax
        /// </summary>
        public static string BuildKeyNoType(long sup, long mat, int tax)
        {
            return string.Format("{0}_{1}_{2}", sup, mat, tax);
        }

        /// <summary>
        /// 不区分是否含税的 key（保留价格类型）：sup_mat_pt
        /// </summary>
        public static string BuildKeyNoTax(long sup, long mat, int pt)
        {
            return string.Format("{0}_{1}_{2}", sup, mat, pt);
        }

        /// <summary>
        /// 不区分是否含税和价格类型的 key：sup_mat
        /// </summary>
        public static string BuildKeyNoTypeNoTax(long sup, long mat)
        {
            return string.Format("{0}_{1}", sup, mat);
        }

        #endregion
    }
}
