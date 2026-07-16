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
        /// 持久化前：读取并填充"价目表含税单价"
        /// </summary>
        public override void BeforeExecuteOperationTransaction(BeforeExecuteOperationTransaction e)
        {
            base.BeforeExecuteOperationTransaction(e);

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

                // 批量查采购入库单业务类型，推导价格类型
                Dictionary<string, string> bizMap = PriceListQueryHelper.GetBusinessTypeMap(this.Context, sourceBillNos);
                foreach (var r in refs)
                {
                    r.PriceType = PriceListQueryHelper.DerivePriceType(r.SourceType, r.SourceBillNo, bizMap);
                }

                // 批量取价
                var reqs = new List<PriceListQueryHelper.PriceReq>();
                foreach (var r in refs)
                {
                    reqs.Add(new PriceListQueryHelper.PriceReq
                    {
                        SupplierId = r.SupplierId,
                        MaterialId = r.MaterialId,
                        PriceType = r.PriceType,
                        IncludedTax = r.IncludedTax
                    });
                }

                Dictionary<string, decimal?> priceMap = PriceListQueryHelper.GetLatestTaxPrice(this.Context, reqs);

                // 写回"价目表含税单价"字段
                foreach (var r in refs)
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
        }
    }
}
