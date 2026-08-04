using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单数据包保存帮助类
    ///
    /// 生成流程（数据包保存，用户确认 2026-08-03）：
    ///   1. 重复校验：客户订单号 F_CustLI_BillNo 已存在（非作废）→ 跳过该份
    ///   2. 基础资料就绪：客户/销售组织/单据类型/销售员/币别/单位 编码查内码
    ///   3. 物料就绪：梯号缺失自动建（JmMaterialHelper），组件缺失报错中止
    ///   4. 构造销售订单数据包（CreateNewBillView）→ 表头 + 明细行赋值 → Save/Submit/Audit
    ///
    /// 明细行 = 每梯级 1 行，物料 = 梯号；单价 = 主轴+上模组+下模组金额之和。
    /// </summary>
    public static class JmSalOrderSaveHelper
    {
        // ==================== 配置区（演示环境需确认真实编码） ====================

        /// <summary>客户编码（通力电梯，演示环境待确认）</summary>
        public const string CustomerNumber = "KONE";

        /// <summary>销售组织编码（演示环境待确认，默认单组织 100）</summary>
        public const string SaleOrgNumber = "100";

        /// <summary>销售订单单据类型编码（默认 XSDD10_SYS）</summary>
        public const string BillTypeNumber = "XSDD10_SYS";

        /// <summary>销售员编码（演示环境待确认）</summary>
        public const string SalerNumber = "0375_SCGL001_1";

        /// <summary>币别编码（PDF CURRENCY 区，如 RMB）</summary>
        public const string DefaultCurrencyNumber = "RMB";

        /// <summary>销售订单单据标识</summary>
        public const string SaleOrderFormId = "SAL_SaleOrder";

        /// <summary>销售订单单据体标识</summary>
        public const string SaleOrderEntryKey = "FSaleOrderEntry";

        // ==================== 保存入口 ====================

        /// <summary>
        /// 保存销售订单（单份 PDF 一张销售订单，多行明细）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="order">PDF 解析结果</param>
        /// <param name="tierNumbers">梯号集合（成品，缺失自动建）</param>
        /// <param name="componentNumbers">组件图号集合（客户负责，缺失报错）</param>
        /// <returns>保存结果（Success=false 时 Message 为原因）</returns>
        public static JmSalOrderSaveResult SaveSaleOrder(
            Context ctx, JmPdfOrder order, List<string> tierNumbers, List<string> componentNumbers)
        {
            JmSalOrderSaveResult result = new JmSalOrderSaveResult();

            string billNo = order.Head != null ? order.Head.BillNo : "";
            if (string.IsNullOrEmpty(billNo))
            {
                result.Message = "PDF 未解析到客户订单号（Purchase order No.），已跳过";
                return result;
            }

            // 1. 重复校验：客户订单号已存在（非作废）→ 跳过
            if (IsBillNoExists(ctx, billNo))
            {
                result.Message = string.Format("客户订单号 {0} 已存在，已跳过（重复）", billNo);
                return result;
            }

            // 2. 基础资料内码查询
            long customerId = QueryBaseDataId(ctx, "T_BD_CUSTOMER", "FCUSTID", CustomerNumber);
            long saleOrgId = QueryBaseDataId(ctx, "T_ORG_ORGANIZATIONS", "FORGID", SaleOrgNumber);
            long billTypeId = QueryBaseDataId(ctx, "T_BAS_BILLTYPE", "FID", BillTypeNumber);
            long salerId = QueryBaseDataId(ctx, "T_BD_SALESMAN", "FSALESMANID", SalerNumber);
            string currencyNumber = string.IsNullOrEmpty(order.Head.Currency)
                ? DefaultCurrencyNumber : order.Head.Currency;
            long currencyId = QueryBaseDataId(ctx, "T_BD_CURRENCY", "FCURRENCYID", currencyNumber);

            if (customerId <= 0 || saleOrgId <= 0 || billTypeId <= 0 || salerId <= 0 || currencyId <= 0)
            {
                result.Message = string.Format(
                    "基础资料编码未配置或未找到（客户 {0}、组织 {1}、单据类型 {2}、销售员 {3}、币别 {4}），请在 JmSalOrderSaveHelper 配置区确认",
                    CustomerNumber, SaleOrgNumber, BillTypeNumber, SalerNumber, currencyNumber);
                return result;
            }

            // 3. 物料就绪：梯号缺失自动建，组件缺失报错中止
            Dictionary<string, long> materialMap = JmMaterialHelper.EnsureMaterials(ctx, tierNumbers, componentNumbers);

            try
            {
                // 4. 构造销售订单数据包
                IBillView view = JmMaterialHelper.CreateNewBillView(ctx, SaleOrderFormId, null);

                // ---- 表头 ----
                view.Model.SetItemValueByID("FBillTypeID", billTypeId.ToString(), 0);
                view.Model.SetItemValueByID("FSaleOrgId", saleOrgId.ToString(), 0);
                view.Model.SetItemValueByID("FCustId", customerId.ToString(), 0);
                view.Model.SetItemValueByID("FSalerId", salerId.ToString(), 0);

                DateTime orderDate;
                if (DateTime.TryParseExact(order.Head.OrderDate, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out orderDate))
                {
                    view.Model.SetValue("FDate", orderDate);
                }

                // 财务信息：结算币别
                view.Model.SetItemValueByID("FSettleCurrId", currencyId.ToString(), 0);

                // 表头自定义字段：客户订单号（重复校验用）/交货条款/合同总额
                view.Model.SetValue("F_CustLI_BillNo", billNo);
                if (!string.IsNullOrEmpty(order.Head.DeliveryTerm))
                {
                    view.Model.SetValue("F_CustLI_DeliveryTerm", order.Head.DeliveryTerm);
                }
                decimal totalAmount = ParseDecimal(order.Head.CurrencyTotal);
                if (totalAmount > 0)
                {
                    view.Model.SetValue("F_CustLI_TotalAmount", totalAmount);
                }

                // ---- 明细行（每梯级 1 行）----
                for (int i = 0; i < order.Tiers.Count; i++)
                {
                    JmPdfTier tier = order.Tiers[i];
                    long materialId = 0;
                    if (materialMap.ContainsKey(tier.TierNo)) materialId = materialMap[tier.TierNo];
                    if (materialId <= 0)
                    {
                        throw new Exception(string.Format("梯号 {0} 物料内码为空，无法生成明细", tier.TierNo));
                    }

                    int row = view.Model.GetEntryRowCount(SaleOrderEntryKey);
                    view.Model.CreateNewEntryRow(SaleOrderEntryKey);

                    view.Model.SetItemValueByID("FMATERIALID", materialId.ToString(), row);
                    view.Model.SetItemValueByID("FSettleOrgIds", saleOrgId.ToString(), row);

                    // 单位（计价单位/单位 = 梯号物料基本单位）
                    long unitId = QueryMaterialBaseUnit(ctx, materialId);
                    if (unitId > 0)
                    {
                        view.Model.SetItemValueByID("FPriceUnitId", unitId.ToString(), row);
                        view.Model.SetItemValueByID("FUnitID", unitId.ToString(), row);
                    }

                    view.Model.SetValue("FQTY", 1m, row);
                    view.Model.SetValue("FPRICE", tier.UnitPrice, row);
                    view.Model.SetValue("FTAXPRICE", tier.UnitPrice, row);
                    view.Model.SetValue("FTAXRATE", 0m, row);
                    view.Model.SetValue("FAMOUNT", tier.UnitPrice, row);

                    // 交期/要货日期（取组件行 Arr.date）
                    string arrDate = FirstArrDate(tier);
                    DateTime deliveryDate;
                    if (DateTime.TryParseExact(arrDate, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out deliveryDate))
                    {
                        view.Model.SetValue("FDELIVERYDATE", deliveryDate, row);
                        view.Model.SetValue("F_CustLI_RequireDate", deliveryDate, row);
                    }

                    // 组件字段（主轴/上模组/下模组）
                    SetComponentFields(view, row, tier);

                    // 工艺字段（特征明确值）
                    SetTechFields(view, row, order.Head);
                }

                // 保存 + 提交 + 审核
                JmMaterialHelper.SaveSubmitAudit(ctx, view, SaleOrderFormId);

                result.Success = true;
                result.BillNo = ObjectToString(view.Model.DataObject["BillNo"]);
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "销售订单保存失败：" + ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 判断客户订单号是否已存在（重复校验用，排除作废状态 B）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="billNo">客户订单号</param>
        /// <returns>已存在返回 true</returns>
        private static bool IsBillNoExists(Context ctx, string billNo)
        {
            string sql = string.Format(
                @"SELECT a1.FID AS FID
                  FROM T_SAL_ORDER a1
                  WHERE a1.F_CustLI_BillNo = '{0}'
                    AND a1.FDocumentStatus <> 'B'",
                billNo.Replace("'", "''"));

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            return rows != null && rows.Count > 0;
        }

        /// <summary>
        /// 设置明细行组件字段（主轴/上模组/下模组各 5 个字段）。
        /// </summary>
        /// <param name="view">单据视图</param>
        /// <param name="row">明细行号</param>
        /// <param name="tier">梯级</param>
        private static void SetComponentFields(IBillView view, int row, JmPdfTier tier)
        {
            if (tier.Axis != null)
            {
                view.Model.SetValue("F_CustLI_AxisCode", tier.Axis.Material, row);
                view.Model.SetValue("F_CustLI_AxisQty", tier.Axis.Qty, row);
                view.Model.SetValue("F_CustLI_AxisPrice", tier.Axis.Price, row);
                view.Model.SetValue("F_CustLI_AxisAmount", tier.Axis.Amount, row);
                view.Model.SetValue("F_CustLI_AxisRev", tier.Axis.Rev, row);
            }
            if (tier.UpModule != null)
            {
                view.Model.SetValue("F_CustLI_UpCode", tier.UpModule.Material, row);
                view.Model.SetValue("F_CustLI_UpQty", tier.UpModule.Qty, row);
                view.Model.SetValue("F_CustLI_UpPrice", tier.UpModule.Price, row);
                view.Model.SetValue("F_CustLI_UpAmount", tier.UpModule.Amount, row);
                view.Model.SetValue("F_CustLI_UpRev", tier.UpModule.Rev, row);
            }
            if (tier.DownModule != null)
            {
                view.Model.SetValue("F_CustLI_DownCode", tier.DownModule.Material, row);
                view.Model.SetValue("F_CustLI_DownQty", tier.DownModule.Qty, row);
                view.Model.SetValue("F_CustLI_DownPrice", tier.DownModule.Price, row);
                view.Model.SetValue("F_CustLI_DownAmount", tier.DownModule.Amount, row);
                view.Model.SetValue("F_CustLI_DownRev", tier.DownModule.Rev, row);
            }
        }

        /// <summary>
        /// 设置明细行工艺字段（仅特征明确值；其余 KM 图号工艺值留空，用户确认 2026-08-03）。
        /// </summary>
        /// <param name="view">单据视图</param>
        /// <param name="row">明细行号</param>
        /// <param name="head">单据头（含工艺字段）</param>
        private static void SetTechFields(IBillView view, int row, JmPdfHead head)
        {
            if (!string.IsNullOrEmpty(head.AntiGrade))
            {
                view.Model.SetValue("F_CustLI_AntiGrade", head.AntiGrade, row);
            }
            if (!string.IsNullOrEmpty(head.UpAssemDraw))
            {
                view.Model.SetValue("F_CustLI_UpAssemDraw", head.UpAssemDraw, row);
            }
            if (!string.IsNullOrEmpty(head.UpAssemDraw2))
            {
                view.Model.SetValue("F_CustLI_UpAssemDraw2", head.UpAssemDraw2, row);
            }
        }

        /// <summary>
        /// 取梯级第一行的到货日期（组件行 Arr.date，各组件行应一致）。
        /// </summary>
        /// <param name="tier">梯级</param>
        /// <returns>日期字符串 dd.MM.yyyy；无返回空串</returns>
        private static string FirstArrDate(JmPdfTier tier)
        {
            if (tier.Axis != null && !string.IsNullOrEmpty(tier.Axis.ArrDate)) return tier.Axis.ArrDate;
            if (tier.UpModule != null && !string.IsNullOrEmpty(tier.UpModule.ArrDate)) return tier.UpModule.ArrDate;
            if (tier.DownModule != null && !string.IsNullOrEmpty(tier.DownModule.ArrDate)) return tier.DownModule.ArrDate;
            return "";
        }

        /// <summary>
        /// 查梯号物料的基本计量单位内码。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="materialId">物料内码</param>
        /// <returns>单位内码；未找到返回 0</returns>
        private static long QueryMaterialBaseUnit(Context ctx, long materialId)
        {
            string sql = string.Format(
                @"SELECT a1.FBaseUnitId AS FBaseUnitId
                  FROM t_BD_MaterialBase a1
                  WHERE a1.FMATERIALID = {0}",
                materialId);

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows != null && rows.Count > 0 && rows[0]["FBaseUnitId"] != null)
            {
                return Convert.ToInt64(rows[0]["FBaseUnitId"]);
            }
            return 0;
        }

        /// <summary>
        /// 按编号查询基础资料内码（复用 JmMaterialHelper 的通用查询）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="tableName">基础资料主表名</param>
        /// <param name="idField">主键字段名</param>
        /// <param name="number">编号</param>
        /// <returns>基础资料内码；未找到返回 0</returns>
        private static long QueryBaseDataId(Context ctx, string tableName, string idField, string number)
        {
            return JmMaterialHelper.QueryBaseDataId(ctx, tableName, idField, number);
        }

        /// <summary>
        /// 金额/数量解析：支持千分位。
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <returns>数值；解析失败返回 0</returns>
        private static decimal ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0m;
            decimal result;
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            return 0m;
        }

        /// <summary>
        /// 对象转字符串（null/DBNull 转空串）。
        /// </summary>
        /// <param name="value">对象</param>
        /// <returns>字符串</returns>
        private static string ObjectToString(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return value.ToString();
        }
    }

    /// <summary>
    /// 吉茂-销售订单保存结果
    /// </summary>
    public class JmSalOrderSaveResult
    {
        /// <summary>是否保存成功</summary>
        public bool Success { get; set; }

        /// <summary>提示信息（失败原因 / 跳过原因）</summary>
        public string Message { get; set; }

        /// <summary>生成的销售订单单据编号（成功时）</summary>
        public string BillNo { get; set; }
    }
}
