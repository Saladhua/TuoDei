using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 暂估应付单 - 列表"获取价目表价格"按钮插件（益讯机械）
    /// 用户在暂估应付单列表勾选（暂存/创建/重新审核状态）单据，点击"获取价目表价格"，
    /// 系统批量按 采购价目表 取最新含税单价，写回单据体"含税单价(FTAXPRICE)"，
    /// 再走标准保存以触发值更新（刷新 税额/价税合计 等），最后保存单据。
    /// 注册位置：AP_Payable 列表表单的 &lt;ListPlugins&gt;。
    /// 说明：暂估应付单与财务应付单共用同一 FormId(AP_Payable)，本插件仅处理立账类型=暂估(2)。
    /// </summary>
    [Description("益讯机械-暂估应付单列表获取价目表价格"), HotUpdate]
    public class YxjTempPayableGetPriceListPlugIn : AbstractListPlugIn
    {
        // 列表按钮 BarItemKey（已在 BOS 列表菜单中建立）
        private const string BarItemGetPrice = "tbGetPrice";

        // 立账类型：2 = 暂估
        private const string AcctTypeTemp = "2";

        // 允许取价的单据状态：暂存(Z) / 创建(A) / 重新审核(D)
        private static readonly HashSet<string> AllowDocStatus = new HashSet<string> { "Z", "A", "B", "D" };

        /// <summary>
        /// 行引用：用于把"取价结果"写回对应的单据体行
        /// </summary>
        private class EntryRef
        {
            public DynamicObject Bill;          // 所属单据
            public DynamicObject Entry;         // 单据体行
            public long SupplierId;              // 供应商内码（表头）
            public long MaterialId;             // 物料内码（行上）
            public bool IncludedTax;            // 是否以含税价录入
            public string SourceType;           // 源单类型(FSOURCETYPE)
            public string SourceBillNo;         // 源单编号(FSourceBillNo)
            public int PriceType;               // 推导后的价格类型（2/3），推导后填充
        }

        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);

            // 仅响应"获取价目表价格"按钮
            if (e.BarItemKey != BarItemGetPrice)
            {
                return;
            }

            // 1. 取选中行主键(FID)
            object[] pkValues = this.ListView.SelectedRowsInfo.GetPrimaryKeyValues();
            if (pkValues == null || pkValues.Length == 0)
            {
                this.View.ShowMessage("请先勾选需要获取价格的暂估应付单。");
                return;
            }

            var ids = new List<long>();
            foreach (object pk in pkValues)
            {
                ids.Add(Convert.ToInt64(pk));
            }

            // 2. 加载单据（AP_Payable 与财务应付单同表单，需按立账类型过滤出暂估）
            BusinessInfo info = this.View.BillBusinessInfo;
            DynamicObject[] bills = BusinessDataServiceHelper.Load(this.Context, ids.Cast<object>().ToArray(), info.GetDynamicObjectType());
            if (bills == null || bills.Length == 0)
            {
                this.View.ShowMessage("加载单据失败，请确认单据状态。");
                return;
            }

            // 3. 收集取价行引用 + 收集源单编号（用于批量推导价格类型）
            var refs = new List<EntryRef>();
            var sourceBillNos = new List<string>();
            var changedBills = new HashSet<DynamicObject>();

            foreach (DynamicObject bill in bills)
            {
                // 仅处理暂估立账类型
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeTemp)
                {
                    continue;
                }

                // 状态限制：仅 暂存 / 创建 / 重新审核
                string docStatus = (bill["DOCUMENTSTATUS"] == null) ? string.Empty : bill["DOCUMENTSTATUS"].ToString();
                if (!AllowDocStatus.Contains(docStatus))
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

                    refs.Add(new EntryRef
                    {
                        Bill = bill,
                        Entry = entry,
                        SupplierId = supplierId,
                        MaterialId = materialId,
                        IncludedTax = includedTax,
                        SourceType = sourceType,
                        SourceBillNo = sourceBillNo
                    });
                }
            }

            if (refs.Count == 0)
            {
                this.View.ShowMessage("所选单据中没有符合取价条件的行（需为暂估、且处于暂存/创建/重新审核状态、含有效物料）。");
                return;
            }

            // 4. 批量查采购入库单业务类型，推导每行价格类型（标准采购/委外）
            Dictionary<string, string> bizMap = PriceListQueryHelper.GetBusinessTypeMap(this.Context, sourceBillNos);
            foreach (var r in refs)
            {
                r.PriceType = PriceListQueryHelper.DerivePriceType(r.SourceType, r.SourceBillNo, bizMap);
            }

            // 5. 构造取价请求并批量取价
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

            // 6. 写回含税单价(FTAXPRICE)，并记录发生变更的单据
            int filledCount = 0;
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
                    r.Entry["TaxPrice"] = price.Value;
                    changedBills.Add(r.Bill);
                    filledCount++;
                }
            }

            if (changedBills.Count == 0)
            {
                this.View.ShowMessage("未在采购价目表中匹配到对应价格，未更新任何单据。");
                return;
            }

            // 7. 走标准保存：触发值更新（刷新 税额 / 价税合计 等）并落库
            BusinessDataServiceHelper.Save(this.Context, info, changedBills.ToArray());

            // 8. 刷新列表并提示结果
            this.View.Refresh();
            this.View.ShowMessage(string.Format("已成功为 {0} 行获取价目表价格并保存。", filledCount));
        }
    }
}
