using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 财务应付单 - 保存时获取采购价目表含税单价并填充到"价目表含税单价"字段（益讯机械）
    /// 注册位置：AP_Payable 的 &lt;OperationServicePlugins&gt;，OperationName = "Save"。
    /// 触发时机：财务应付单（采购发票）保存时。
    /// 仅对 立账类型 = 财务(3) 的单据生效；暂估(2) 不处理。
    /// 取价规则与暂估应付单一致：供应商 + 物料 + 价格类型(标准采购/委外) + 是否含税，
    /// 按生效日期取最新价目表的含税单价(FTAXPRICE)。
    /// </summary>
    [Description("益讯机械-财务应付单保存取价填价目表含税单价"), HotUpdate]
    public class YxjAPPayableSaveGetPricePlugIn : AbstractOperationServicePlugIn
    {
        // 立账类型：3 = 财务
        private const string AcctTypeFinance = "3";

        /// <summary>
        /// 行引用：保存取价结果，并用于推导价格类型
        /// </summary>
        private class SaveEntryRef
        {
            public DynamicObject Entry;     // 单据体行
            public long SupplierId;          // 供应商内码（表头）
            public long MaterialId;          // 物料内码（行上）
            public bool IncludedTax;         // 是否以含税价录入
            public string SourceType;        // 源单类型(FSOURCETYPE)
            public string SourceBillNo;      // 源单编号(FSourceBillNo)
            public int PriceType;            // 推导后的价格类型（2/3）
        }

        /// <summary>
        /// 声明本次操作需要从 DB 加载的字段（性能约定：只加需要的字段）
        /// </summary>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);

            e.FieldKeys.Add("FSETACCOUNTTYPE");            // 立账类型：区分暂估/财务
            e.FieldKeys.Add("ISTAX");                     // 是否以含税价录入
            e.FieldKeys.Add("FSUPPLIERID");                // 供应商（表头）
            e.FieldKeys.Add("AP_PAYABLEENTRY");           // 单据体
            e.FieldKeys.Add("AP_PAYABLEENTRY.FMATERIALID");
            e.FieldKeys.Add("AP_PAYABLEENTRY.FSOURCETYPE");
            e.FieldKeys.Add("AP_PAYABLEENTRY.SourceBillNo");
            e.FieldKeys.Add("AP_PAYABLEENTRY.IsFree");
            e.FieldKeys.Add("F_CustLi_PriceListTaxPrice"); // 新字段：价目表含税单价
        }

        /// <summary>
        /// 保存操作事务内：读取采购价目表含税单价并填充到"价目表含税单价"字段
        /// 注意：平台会自动将本事件中对数据包的修改保存到数据库
        /// </summary>
        public override void BeginOperationTransaction(BeginOperationTransactionArgs e)
        {
            base.BeginOperationTransaction(e);

            foreach (DynamicObject bill in e.DataEntitys)
            {
                // 仅财务应付单（立账类型 = 3）
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeFinance)
                {
                    continue;
                }

                long supplierId = (bill["SupplierId_ID"] == null) ? 0L : Convert.ToInt64(bill["SupplierId_ID"]);
                bool includedTax = (bill["ISTAX"] != null) && Convert.ToBoolean(bill["ISTAX"]);

                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null)
                {
                    continue;
                }

                // 收集取价行 + 源单编号
                var refs = new List<SaveEntryRef>();
                var sourceBillNos = new List<string>();

                foreach (DynamicObject entry in entryObjs)
                {
                    // 赠品不取价
                    if (entry["IsFree"] != null && Convert.ToBoolean(entry["IsFree"]))
                    {
                        continue;
                    }

                    long materialId = (entry["MaterialId_Id"] == null) ? 0L : Convert.ToInt64(entry["MaterialId_Id"]);
                    if (materialId == 0L)
                    {
                        continue;
                    }

                    string sourceType = (entry["FSOURCETYPE"] == null) ? string.Empty : entry["FSOURCETYPE"].ToString();
                    string sourceBillNo = (entry["SourceBillNo"] == null) ? string.Empty : entry["SourceBillNo"].ToString();

                    if (!string.IsNullOrEmpty(sourceBillNo) && !sourceBillNos.Contains(sourceBillNo))
                    {
                        sourceBillNos.Add(sourceBillNo);
                    }

                    refs.Add(new SaveEntryRef
                    {
                        Entry = entry,
                        SupplierId = supplierId,
                        MaterialId = materialId,
                        IncludedTax = includedTax,
                        SourceType = sourceType,
                        SourceBillNo = sourceBillNo
                    });
                }

                if (refs.Count == 0)
                {
                    continue;
                }

                // 追溯源单：财务应付单 → 暂估应付单 → 入库单
                var apBillNos = new List<string>();
                foreach (var r in refs)
                {
                    if (r.SourceType == "AP_Payable" && !string.IsNullOrEmpty(r.SourceBillNo)
                        && !apBillNos.Contains(r.SourceBillNo))
                    {
                        apBillNos.Add(r.SourceBillNo);
                    }
                }

                if (apBillNos.Count > 0)
                {
                    Dictionary<string, string[]> tempSrcMap = PriceListQueryHelper.GetTempPayableSourceMap(this.Context, apBillNos);
                    foreach (var r in refs)
                    {
                        if (r.SourceType == "AP_Payable" && !string.IsNullOrEmpty(r.SourceBillNo))
                        {
                            string key = string.Format("{0}_{1}", r.SourceBillNo, r.MaterialId);
                            if (tempSrcMap.TryGetValue(key, out string[] originalSrc))
                            {
                                r.SourceType = originalSrc[0];
                                r.SourceBillNo = originalSrc[1];
                            }
                        }
                    }
                }

                // 分出已追溯行(可推导价格类型) 和 未追溯行(兜底取价)
                var resolvedRefs = new List<SaveEntryRef>();
                var unresolvedRefs = new List<SaveEntryRef>();
                foreach (var r in refs)
                {
                    if (r.SourceType == "AP_Payable")
                        unresolvedRefs.Add(r);
                    else
                        resolvedRefs.Add(r);
                }

                // 已追溯行：按价格类型取价
                if (resolvedRefs.Count > 0)
                {
                    var resolvedBillNos = new List<string>();
                    foreach (var r in resolvedRefs)
                        if (!string.IsNullOrEmpty(r.SourceBillNo) && !resolvedBillNos.Contains(r.SourceBillNo))
                            resolvedBillNos.Add(r.SourceBillNo);

                    Dictionary<string, string> bizMap = PriceListQueryHelper.GetBusinessTypeMap(this.Context, resolvedBillNos);
                    var reqs = new List<PriceListQueryHelper.PriceReq>();
                    foreach (var r in resolvedRefs)
                    {
                        r.PriceType = PriceListQueryHelper.DerivePriceType(r.SourceType, r.SourceBillNo, bizMap);
                        reqs.Add(new PriceListQueryHelper.PriceReq
                        {
                            SupplierId = r.SupplierId,
                            MaterialId = r.MaterialId,
                            PriceType = r.PriceType,
                            IncludedTax = r.IncludedTax
                        });
                    }

                    Dictionary<string, decimal?> priceMap = PriceListQueryHelper.GetLatestTaxPrice(this.Context, reqs);
                    foreach (var r in resolvedRefs)
                    {
                        var req = new PriceListQueryHelper.PriceReq
                        {
                            SupplierId = r.SupplierId,
                            MaterialId = r.MaterialId,
                            PriceType = r.PriceType,
                            IncludedTax = r.IncludedTax
                        };

                        if (priceMap.TryGetValue(PriceListQueryHelper.BuildKey(req), out decimal? price) && price.HasValue)
                        {
                            r.Entry["F_CustLi_PriceListTaxPrice"] = price.Value;
                        }
                    }
                }

                // 未追溯行（无源单信息）：按供应商+物料取最新价格（不区分价格类型）
                if (unresolvedRefs.Count > 0)
                {
                    var reqs = new List<PriceListQueryHelper.PriceReq>();
                    foreach (var r in unresolvedRefs)
                    {
                        reqs.Add(new PriceListQueryHelper.PriceReq
                        {
                            SupplierId = r.SupplierId,
                            MaterialId = r.MaterialId,
                            PriceType = 0,
                            IncludedTax = r.IncludedTax
                        });
                    }

                    Dictionary<string, decimal?> priceMap = PriceListQueryHelper.GetLatestTaxPriceAnyType(this.Context, reqs);
                    foreach (var r in unresolvedRefs)
                    {
                        string anyKey = PriceListQueryHelper.BuildKeyNoType(
                            r.SupplierId, r.MaterialId, r.IncludedTax ? 1 : 0);
                        if (priceMap.TryGetValue(anyKey, out decimal? price) && price.HasValue)
                        {
                            r.Entry["F_CustLi_PriceListTaxPrice"] = price.Value;
                        }
                    }
                }
            }
        }
    }
}
