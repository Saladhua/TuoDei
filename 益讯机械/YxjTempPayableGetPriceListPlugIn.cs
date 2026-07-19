using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Core.DynamicForm.Operation;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Core.Validation;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("益讯机械-暂估应付单列表获取价目表价格"), HotUpdate]
    public class YxjTempPayableGetPriceListPlugIn : AbstractListPlugIn
    {
        private const string BarItemGetPrice = "tbGetPrice";
        private const string AcctTypeTemp = "2";
        private readonly HashSet<string> AllowDocStatus = new HashSet<string> { "Z", "A", "B", "D" };

        private class EntryRef
        {
            public DynamicObject Bill;
            public DynamicObject Entry;
            public long SupplierId;
            public long MaterialId;
            public bool IncludedTax;
            public string SourceType;
            public string SourceBillNo;
            public int PriceType;
        }

        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);



            if (e.BarItemKey != BarItemGetPrice)
                return;

            object[] pkValues = this.ListView.SelectedRowsInfo.GetPrimaryKeyValues();
            if (pkValues == null || pkValues.Length == 0)
            {
                this.View.ShowMessage("请先勾选需要获取价格的暂估应付单。");
                return;
            }

            var ids = pkValues.Select(pk => Convert.ToInt64(pk)).ToList();

            BusinessInfo info = this.View.BillBusinessInfo;
            string idFilter = string.Format("FID IN ({0})", string.Join(",", ids));
            OQLFilter oqlFilter = OQLFilter.CreateHeadEntityFilter(idFilter);
            DynamicObject[] bills = BusinessDataServiceHelper.Load(this.Context, info, null, oqlFilter);
            if (bills == null || bills.Length == 0)
            {
                this.View.ShowMessage("加载单据失败，请确认单据状态。");
                return;
            }

            var refs = new List<EntryRef>();
            var sourceBillNos = new List<string>();
            var changedBills = new HashSet<DynamicObject>();

            foreach (DynamicObject bill in bills)
            {
                string acctType = (bill["FSETACCOUNTTYPE"] == null) ? string.Empty : bill["FSETACCOUNTTYPE"].ToString();
                if (acctType != AcctTypeTemp)
                    continue;

                string docStatus = (bill["DOCUMENTSTATUS"] == null) ? string.Empty : bill["DOCUMENTSTATUS"].ToString();
                if (!AllowDocStatus.Contains(docStatus))
                    continue;

                long supplierId = (bill["SupplierId_ID"] == null) ? 0L : Convert.ToInt64(bill["SupplierId_ID"]);
                bool includedTax = (bill["ISTAX"] != null) && Convert.ToBoolean(bill["ISTAX"]);

                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null)
                    continue;

                foreach (DynamicObject entry in entryObjs)
                {
                    if (entry["IsFree"] != null && Convert.ToBoolean(entry["IsFree"]))
                        continue;

                    long materialId = (entry["MaterialId_Id"] == null) ? 0L : Convert.ToInt64(entry["MaterialId_Id"]);
                    if (materialId == 0L)
                        continue;

                    string sourceType = (entry["FSOURCETYPE"] == null) ? string.Empty : entry["FSOURCETYPE"].ToString();
                    string sourceBillNo = (entry["SourceBillNo"] == null) ? string.Empty : entry["SourceBillNo"].ToString();

                    if (!string.IsNullOrEmpty(sourceBillNo) && !sourceBillNos.Contains(sourceBillNo))
                        sourceBillNos.Add(sourceBillNo);

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
                this.View.ShowMessage("所选单据中没有符合取价条件的行。");
                return;
            }

            Dictionary<string, string> bizMap = PriceListQueryHelper.GetBusinessTypeMap(this.Context, sourceBillNos);
            foreach (var r in refs)
                r.PriceType = PriceListQueryHelper.DerivePriceType(r.SourceType, r.SourceBillNo, bizMap);

            var reqs = refs.Select(r => new PriceListQueryHelper.PriceReq
            {
                SupplierId = r.SupplierId,
                MaterialId = r.MaterialId,
                PriceType = r.PriceType,
                IncludedTax = r.IncludedTax
            }).ToList();

            Dictionary<string, decimal?> priceMap =
                PriceListQueryHelper.GetLatestTaxPrice(this.Context, reqs);

            int filledCount = 0;
            foreach (var r in refs)
            {
                string key = PriceListQueryHelper.BuildKey(
                    r.SupplierId, r.MaterialId, r.PriceType, r.IncludedTax ? 1 : 0);

                if (!priceMap.TryGetValue(key, out decimal? priceValue) || !priceValue.HasValue)
                    continue;

                decimal qty = Convert.ToDecimal(r.Entry["FPRICEQTY"] ?? 0m);
                if (qty == 0m)
                    continue;

                decimal taxRate = Convert.ToDecimal(r.Entry["EntryTaxRate"] ?? 0m);

                decimal rawPrice = priceValue.Value;

                if (r.IncludedTax)
                {
                    decimal taxPrice = Math.Round(rawPrice, 6);
                    decimal unitPrice = taxRate > 0m
                        ? Math.Round(taxPrice / (1m + taxRate / 100m), 6)
                        : taxPrice;

                    r.Entry["FTaxPrice"] = taxPrice;
                    r.Entry["FPrice"] = unitPrice;
                }
                else
                {
                    decimal unitPrice = Math.Round(rawPrice, 6);
                    decimal taxPrice = taxRate > 0m
                        ? Math.Round(unitPrice * (1m + taxRate / 100m), 6)
                        : unitPrice;

                    r.Entry["FPrice"] = unitPrice;
                    r.Entry["FTaxPrice"] = taxPrice;
                }

                r.Entry["EntryTaxRate"] = taxRate;

                decimal entryTaxPrice = Convert.ToDecimal(r.Entry["FTaxPrice"]);
                decimal entryPrice = Convert.ToDecimal(r.Entry["FPrice"]);
                r.Entry["FAmount"] = Math.Round(qty * entryTaxPrice, 6);
                r.Entry["FTaxAmount"] = Math.Round(qty * (entryTaxPrice - entryPrice), 6);

                changedBills.Add(r.Bill);
                filledCount++;
            }

            foreach (DynamicObject bill in changedBills)
            {
                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null) continue;

                decimal headAmount = 0m;
                decimal headTaxAmount = 0m;

                foreach (DynamicObject entry in entryObjs)
                {
                    headAmount += Convert.ToDecimal(entry["FAmount"] ?? 0m);
                    headTaxAmount += Convert.ToDecimal(entry["FTaxAmount"] ?? 0m);
                }

                bill["FALLAMOUNT"] = Math.Round(headAmount, 6);

                decimal headAmountFor = Math.Round(headAmount, 6);
                decimal headTaxAmountFor = Math.Round(headTaxAmount, 6);
                bill["FALLAMOUNTFOR"] = headAmountFor;
                bill["FTAXAMOUNTFOR"] = headTaxAmountFor;
                bill["FPRICEFOR"] = Math.Round(headAmountFor - headTaxAmountFor, 6);

                var payPlan = bill["AP_PAYABLEPLAN"] as DynamicObjectCollection;
                if (payPlan != null)
                {
                    payPlan.Clear();
                    DynamicObject newPlan = new DynamicObject(payPlan.DynamicCollectionItemPropertyType);
                    newPlan["ENDDATE"] = bill["FENDDATE_H"];
                    newPlan["PAYAMOUNTFOR"] = headAmountFor;
                    newPlan["FPAYRATE"] = 100m;
                    newPlan["PAYAMOUNT"] = headAmountFor;
                    payPlan.Add(newPlan);
                }
            }

            if (changedBills.Count == 0)
            {
                this.View.ShowMessage("未在采购价目表中匹配到对应价格，未更新任何单据。");
                return;
            }

            DynamicObject[] dataObjects = changedBills.ToArray();
            IOperationResult saveResult = BusinessDataServiceHelper.Save(this.Context, info, dataObjects);

            if (saveResult.IsSuccess)
            {
                this.View.Refresh();
                this.View.ShowMessage(string.Format("已成功为 {0} 行获取价目表价格并保存。", filledCount));
            }
            else
            {
                FormatOperateResultValidationInfo(saveResult);
                this.View.ShowOperateResult(saveResult.OperateResult);
            }
        }

        protected virtual void FormatOperateResultValidationInfo(IOperationResult result)
        {
            if (result.ValidationErrors == null || result.ValidationErrors.Count == 0)
                return;

            var collection = result.OperateResult;
            foreach (var errorInfo in result.ValidationErrors)
            {
                var rs = new OperateResult
                {
                    PKValue = errorInfo.BillPKID,
                    RowIndex = errorInfo.RowIndex,
                    Name = errorInfo.Title,
                    SuccessStatus = false,
                    Message = errorInfo.Message,
                    MessageType = errorInfo.Level == ErrorLevel.Warning ? MessageType.Warning : MessageType.FatalError
                };
                collection.Add(rs);
            }
        }
    }
}
