using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
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
            var entryUpdates = new Dictionary<long, (string taxPrice, string price, string allAmt, string noTaxAmt, string taxAmt)>();
            var headerTotals = new Dictionary<long, (decimal allAmt, decimal taxAmt, decimal noTaxAmt)>();
            var billIdMap = new Dictionary<DynamicObject, long>();

            foreach (var r in refs)
            {
                string key = PriceListQueryHelper.BuildKey(
                    r.SupplierId, r.MaterialId, r.PriceType, r.IncludedTax ? 1 : 0);

                if (!priceMap.TryGetValue(key, out decimal? priceValue) || !priceValue.HasValue)
                    continue;

                decimal qty = Convert.ToDecimal(r.Entry["PriceQty"] ?? 0m);
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

                    r.Entry["TaxPrice"] = taxPrice;
                    r.Entry["FPrice"] = unitPrice;
                }
                else
                {
                    decimal unitPrice = Math.Round(rawPrice, 6);
                    decimal taxPrice = taxRate > 0m
                        ? Math.Round(unitPrice * (1m + taxRate / 100m), 6)
                        : unitPrice;

                    r.Entry["FPrice"] = unitPrice;
                    r.Entry["TaxPrice"] = taxPrice;
                }

                r.Entry["EntryTaxRate"] = taxRate;

                decimal entryTaxPrice = Convert.ToDecimal(r.Entry["TaxPrice"]);
                decimal entryPrice = Convert.ToDecimal(r.Entry["FPrice"]);
                decimal allAmt = Math.Round(qty * entryTaxPrice, 6);
                decimal noTaxAmt = Math.Round(qty * entryPrice, 6);
                decimal taxAmt = Math.Round(allAmt - noTaxAmt, 6);

                r.Entry["FALLAMOUNTFOR_D"] = allAmt;
                r.Entry["FNoTaxAmountFor_D"] = noTaxAmt;
                r.Entry["NOTAXAMOUNT"] = noTaxAmt;
                r.Entry["FTAXAMOUNTFOR_D"] = taxAmt;

                long entryId = Convert.ToInt64(r.Entry["Id"]);
                long billId = Convert.ToInt64(r.Bill["Id"]);

                entryUpdates[entryId] = (
                    entryTaxPrice.ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                    entryPrice.ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                    allAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    noTaxAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    taxAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

                if (headerTotals.ContainsKey(billId))
                {
                    var old = headerTotals[billId];
                    headerTotals[billId] = (old.allAmt + allAmt, old.taxAmt + taxAmt, old.noTaxAmt + noTaxAmt);
                }
                else
                    headerTotals[billId] = (allAmt, taxAmt, noTaxAmt);

                billIdMap[r.Bill] = billId;
                changedBills.Add(r.Bill);
                filledCount++;
            }

            foreach (DynamicObject bill in changedBills)
            {
                var entryObjs = bill["AP_PAYABLEENTRY"] as DynamicObjectCollection;
                if (entryObjs == null) continue;

                decimal totalAmountFor = 0m;

                foreach (DynamicObject entry in entryObjs)
                {
                    totalAmountFor += Convert.ToDecimal(entry["FALLAMOUNTFOR_D"] ?? 0m);
                }

                var payPlan = bill["AP_PAYABLEPLAN"] as DynamicObjectCollection;
                if (payPlan != null)
                {
                    payPlan.Clear();
                    DynamicObject newPlan = new DynamicObject(payPlan.DynamicCollectionItemPropertyType);
                    newPlan["ENDDATE"] = bill["FENDDATE_H"];
                    newPlan["PAYAMOUNTFOR"] = Math.Round(totalAmountFor, 6);
                    newPlan["FPAYRATE"] = 100m;
                    newPlan["PAYAMOUNT"] = Math.Round(totalAmountFor, 6);
                    payPlan.Add(newPlan);
                }
            }

            if (changedBills.Count == 0)
            {
                this.View.ShowMessage("未在采购价目表中匹配到对应价格，未更新任何单据。");
                return;
            }

            DynamicObject[] dataObjects = changedBills.ToArray();

            // Step 1 - Save前写死金额
            if (entryUpdates.Count > 0)
            {
                var sb = new StringBuilder();

                string entryIdList = string.Join(",", entryUpdates.Keys);
                string billIdList = string.Join(",", headerTotals.Keys);

                sb.Append("UPDATE T_AP_PAYABLEENTRY SET ");
                sb.Append("FTAXPRICE = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.taxPrice);
                sb.Append("END, ");
                sb.Append("FPrice = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.price);
                sb.Append("END, ");
                sb.Append("FALLAMOUNTFOR = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt);
                sb.Append("END, ");
                sb.Append("FNoTaxAmountFor = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.noTaxAmt);
                sb.Append("END, ");
                sb.Append("FTAXAMOUNTFOR = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.taxAmt);
                sb.Append("END, ");
                sb.Append("FALLAMOUNT = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt);
                sb.Append("END, ");
                sb.Append("FTAXAMOUNT = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.taxAmt);
                sb.Append("END, ");
                sb.Append("FNOTAXAMOUNT = CASE FENTRYID ");
                foreach (var kv in entryUpdates)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.noTaxAmt);
                sb.Append("END ");
                sb.AppendFormat("WHERE FENTRYID IN ({0}) AND FID IN ({1});", entryIdList, billIdList);

                sb.Append("UPDATE T_AP_PAYABLE SET ");
                sb.Append("FALLAMOUNTFOR = CASE FID ");
                foreach (var kv in headerTotals)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("END ");
                sb.AppendFormat("WHERE FID IN ({0});", billIdList);

                sb.Append("UPDATE T_AP_PAYABLEFIN SET ");
                sb.Append("FALLAMOUNT = CASE FID ");
                foreach (var kv in headerTotals)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("END, ");
                sb.Append("FTAXAMOUNT = CASE FID ");
                foreach (var kv in headerTotals)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.taxAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("END, ");
                sb.Append("FNOTAXAMOUNT = CASE FID ");
                foreach (var kv in headerTotals)
                    sb.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.noTaxAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("END ");
                sb.AppendFormat("WHERE FID IN ({0});", billIdList);

                DBServiceHelper.ExecuteDataSet(this.Context, sb.ToString());
            }

            IOperationResult saveResult = BusinessDataServiceHelper.Save(this.Context, info, dataObjects);

            // Step 2 - Save后更新付款计划
            if (saveResult.IsSuccess)
            {
                if (headerTotals.Count > 0)
                {
                    var sbPlan = new StringBuilder();
                    string billIdList = string.Join(",", headerTotals.Keys);

                    sbPlan.Append("UPDATE T_AP_PAYABLEPLAN SET ");
                    sbPlan.Append("FPAYAMOUNTFOR = CASE FID ");
                    foreach (var kv in headerTotals)
                        sbPlan.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    sbPlan.Append("END, ");
                    sbPlan.Append("FPAYAMOUNT = CASE FID ");
                    foreach (var kv in headerTotals)
                        sbPlan.AppendFormat("WHEN {0} THEN {1} ", kv.Key, kv.Value.allAmt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    sbPlan.Append("END ");
                    sbPlan.AppendFormat("WHERE FID IN ({0});", billIdList);

                    DBServiceHelper.ExecuteDataSet(this.Context, sbPlan.ToString());
                }

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
