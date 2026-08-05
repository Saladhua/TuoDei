using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单数据包保存帮助类
    ///
    /// 生成流程（数据包保存，用户确认 2026-08-03）：
    ///   1. 重复校验：客户订单号 F_CustLI_BillNo 已存在（非作废）→ 跳过该份
    ///   2. 基础资料就绪：客户按 PDF Buyer 名称查内码；销售组织/单据类型/销售员/币别 按编码查内码后 SetItemValueByID
    ///   3. 物料就绪：梯号缺失自动建（JmMaterialHelper），组件缺失报错中止
    ///   4. 构造销售订单数据包（CreateNewBillView）→ 表头 + 明细行赋值 → Save（仅保存）
    ///
    /// 引用字段统一用 SetItemValueByID（金蝶内部处理 long/GUID 内码，禁止 long.Parse 手动转换）；
    /// 明细行号用 CreateNewEntryRow 返回值（从 0 开始），不得用 GetEntryRowCount 前置计数。
    ///
    /// 明细行 = 每梯级 1 行，物料 = 梯号；单价 = 主轴+上模组+下模组金额之和。
    /// </summary>
    public static class JmSalOrderSaveHelper
    {
        // ==================== 配置区（演示环境需确认真实编码） ====================

        /// <summary>销售组织编码（写死值 100）</summary>
        public const string SaleOrgNumber = "100";

        /// <summary>销售订单单据类型编码（写死值 XSDD01_SYS）</summary>
        public const string BillTypeNumber = "XSDD01_SYS";

        /// <summary>销售员编码（写死值 0001_GW000001_1）</summary>
        public const string SalerNumber = "0001_GW000001_1";

        /// <summary>币别编码（写死值 PRE001；PDF CURRENCY 区未取到时使用）</summary>
        public const string DefaultCurrencyNumber = "PRE001";

        /// <summary>销售订单单据标识</summary>
        public const string SaleOrderFormId = "SAL_SaleOrder";

        /// <summary>销售订单单据体实体键（CreateNewEntryRow/GetEntryRowCount 使用，元数据实体键带 F 前缀）</summary>
        public const string SaleOrderEntryKey = "FSaleOrderEntry";

        /// <summary>销售订单单据体集合属性名（DataObject["..."] 使用，不带 F 前缀，见林蓝 2026-07-28 日志）</summary>
        public const string SaleOrderEntryCollectionKey = "SaleOrderEntry";

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

            // 2. 基础资料：客户按名称查内码（活的）；币别取 PDF（未取到用写死值）
            long customerId = QueryBaseDataByName(ctx, "T_BD_CUSTOMER_L", "FCUSTID", order.Head.Customer);
            string currencyNumber = string.IsNullOrEmpty(order.Head.Currency)
                ? DefaultCurrencyNumber : order.Head.Currency;

            if (customerId <= 0)
            {
                result.Message = string.Format("客户 {0} 在系统中未找到（按名称 T_BD_CUSTOMER_L.FNAME 查询），已跳过", order.Head.Customer);
                return result;
            }

            // 3. 物料就绪：梯号缺失自动建，组件缺失报错中止
            Dictionary<string, long> materialMap = JmMaterialHelper.EnsureMaterials(ctx, tierNumbers, componentNumbers);

            try
            {
                // 4. 构造销售订单数据包
                IBillView view = JmMaterialHelper.CreateNewBillView(ctx, SaleOrderFormId, null);

                // 数据包保存标准模式：必须 FireOnLoad 初始化 DataObject（林蓝样板/skills create-billview-pattern 红线）
                DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
                proxy.FireOnLoad();

                // ---- 表头 ----
                // 组织/单据类型/销售员/币别：查内码 + SetItemValueByID（基础资料引用字段赋值，金蝶内部处理 long/GUID）
                string billTypeId = JmMaterialHelper.QueryBaseDataId(ctx, "T_BAS_BILLTYPE", "FBILLTYPEID", "FNumber", BillTypeNumber);
                string saleOrgId = JmMaterialHelper.QueryBaseDataId(ctx, "T_ORG_ORGANIZATIONS", "FORGID", "FNumber", SaleOrgNumber);
                string salerId = JmMaterialHelper.QueryBaseDataId(ctx, "T_BD_OPERATORENTRY", "FENTRYID", "FNUMBER", SalerNumber);
                string settleCurrId = JmMaterialHelper.QueryBaseDataId(ctx, "T_BD_CURRENCY", "FCURRENCYID", "FNumber", currencyNumber);
                // 结算组织 = 销售组织（循环外一次性查询，避免循环内查 DB）
                string settleOrgId = JmMaterialHelper.QueryBaseDataId(ctx, "T_ORG_ORGANIZATIONS", "FORGID", "FNumber", SaleOrgNumber);

                if (!string.IsNullOrEmpty(billTypeId))
                {
                    view.Model.SetItemValueByID("FBillTypeID", billTypeId, 0);
                }
                if (!string.IsNullOrEmpty(saleOrgId))
                {
                    view.Model.SetItemValueByID("FSaleOrgId", saleOrgId, 0);
                }
                if (!string.IsNullOrEmpty(salerId))
                {
                    view.Model.SetItemValueByID("FSalerId", salerId, 0);
                }
                if (!string.IsNullOrEmpty(settleCurrId))
                {
                    view.Model.SetItemValueByID("FSettleCurrId", settleCurrId, 0);
                }
                view.Model.SetItemValueByID("FCustId", customerId.ToString(), 0);

                DateTime orderDate;
                if (DateTime.TryParseExact(order.Head.OrderDate, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out orderDate))
                {
                    view.Model.SetValue("FDate", orderDate);
                }

                // 表头自定义字段：客户订单号（重复校验用）
                view.Model.SetValue("F_CustLI_BillNo", billNo);

                // 销售订单汇率（必录，用户确认固定 1）
                view.Model.SetValue("FExchangeRate", 1m);

                // ---- 明细行（每梯级 1 行）----
                // FireOnLoad 后新增单据预置 1 个空明细行，必须清空，否则 CreateNewEntryRow 后行号从 1 开始，
                // 第 0 行空行会触发保存必填（SKILL.md:130 entryCol.Clear 标准写法）
                DynamicObjectCollection entryCol = view.Model.DataObject[SaleOrderEntryCollectionKey] as DynamicObjectCollection;
                if (entryCol != null)
                {
                    entryCol.Clear();
                }

                for (int i = 0; i < order.Tiers.Count; i++)
                {
                    JmPdfTier tier = order.Tiers[i];
                    long materialId = 0;
                    if (materialMap.ContainsKey(tier.TierNo)) materialId = materialMap[tier.TierNo];
                    if (materialId <= 0)
                    {
                        throw new Exception(string.Format("梯号 {0} 物料内码为空，无法生成明细", tier.TierNo));
                    }

                    // CreateNewEntryRow 返回 void；新明细行号 = 创建后 GetEntryRowCount - 1（从 0 开始）
                    view.Model.CreateNewEntryRow(SaleOrderEntryKey);
                    int row = view.Model.GetEntryRowCount(SaleOrderEntryKey) - 1;

                    view.Model.SetItemValueByID("FMATERIALID", materialId.ToString(), row);

                    // 结算组织（基础资料引用字段，SetItemValueByID）
                    if (!string.IsNullOrEmpty(settleOrgId))
                    {
                        view.Model.SetItemValueByID("FSettleOrgIds", settleOrgId, row);
                    }

                    // 单位（计价单位/销售单位/库存单位 = 梯号物料基本单位）
                    long unitId = QueryMaterialBaseUnit(ctx, materialId);
                    if (unitId > 0)
                    {
                        view.Model.SetItemValueByID("FPriceUnitId", unitId.ToString(), row);
                        view.Model.SetItemValueByID("FUnitID", unitId.ToString(), row);
                        view.Model.SetItemValueByID("FSTOCKUNITID", unitId.ToString(), row);
                    }

                    // 库存组织（= 销售组织）
                    if (!string.IsNullOrEmpty(saleOrgId))
                    {
                        view.Model.SetItemValueByID("FStockOrgId", saleOrgId, row);
                    }

                    view.Model.SetValue("FQTY", 1m, row);
                    view.Model.SetValue("FBaseUnitQty", 1m, row);    // 销售基本数量 = 销售数量（演示环境基本单位=销售单位，换算 1:1，避免下推生产订单因基本数量为0被拦）
                    view.Model.SetValue("FPRICEUNITQTY", 1m, row);   // 计价数量 = 销售数量
                    view.Model.SetValue("FPRICE", tier.UnitPrice, row);
                    view.Model.SetValue("FTAXPRICE", tier.UnitPrice, row);
                    view.Model.SetValue("FEntryTaxRate", 0m, row);
                    view.Model.SetValue("FAMOUNT", tier.UnitPrice, row);

                    // 交期/要货日期（取组件行 Arr.date）
                    string arrDate = FirstArrDate(tier);
                    DateTime deliveryDate;
                    if (DateTime.TryParseExact(arrDate, "dd.MM.yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out deliveryDate))
                    {
                        view.Model.SetValue("FDELIVERYDATE", deliveryDate, row);
                    }

                    // 组件字段（主轴/上模组/下模组）
                    SetComponentFields(view, row, tier);

                    // 工艺字段（特征明确值）
                    SetTechFields(view, row, order.Head);
                }

                // 仅保存（不提交/不审核，用户确认 2026-08-04）
                JmMaterialHelper.SaveOnly(ctx, view, SaleOrderFormId);

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
        /// <param name="row">明细行号（从 0 开始）</param>
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
        /// <param name="row">明细行号（从 0 开始）</param>
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
        /// 按名称查询基础资料内码（客户用，PDF Buyer 名称匹配 FNAME）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="tableName">基础资料主表名</param>
        /// <param name="idField">主键字段名</param>
        /// <param name="name">名称</param>
        /// <returns>基础资料内码；未找到返回 0</returns>
        private static long QueryBaseDataByName(Context ctx, string tableName, string idField, string name)
        {
            return JmMaterialHelper.QueryBaseDataByName(ctx, tableName, idField, name);
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
