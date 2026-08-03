using System.Collections.Generic;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单导入 · 采购合同PDF解析结果模型
    /// 一份 PDF（一张采购订单）解析后对应一个 JmPdfOrder：
    ///   单据头（订单号/日期/币别/总额/防腐等级/组装图）+ 多个梯级（每梯级聚合主轴/上模组/下模组 3 个组件行）。
    /// </summary>
    public class JmPdfOrder
    {
        /// <summary>解析是否成功</summary>
        public bool Success { get; set; }

        /// <summary>解析失败原因（Success=false 时有效）</summary>
        public string ErrorMessage { get; set; }

        /// <summary>单据头解析结果</summary>
        public JmPdfHead Head { get; set; }

        /// <summary>梯级集合（每梯级 = 1 条销售订单明细，物料 = 梯号）</summary>
        public List<JmPdfTier> Tiers { get; set; }

        public JmPdfOrder()
        {
            Head = new JmPdfHead();
            Tiers = new List<JmPdfTier>();
        }
    }

    /// <summary>
    /// 吉茂-PDF 单据头字段（关键字驱动提取）
    /// </summary>
    public class JmPdfHead
    {
        /// <summary>客户订单号（Purchase order No.，重复校验用），如 4800761812</summary>
        public string BillNo { get; set; }

        /// <summary>订单日期（Date），原始格式 dd.MM.yyyy</summary>
        public string OrderDate { get; set; }

        /// <summary>币别（CURRENCY 区），如 RMB</summary>
        public string Currency { get; set; }

        /// <summary>合同总额（CURRENCY TOTAL AMOUNT），如 88,556.52</summary>
        public string CurrencyTotal { get; set; }

        /// <summary>交货条款（Terms of delivery），如 DAP Kunshan Factory</summary>
        public string DeliveryTerm { get; set; }

        /// <summary>扶梯防腐等级（特征明确值），如 C4-High</summary>
        public string AntiGrade { get; set; }

        /// <summary>上模组组装图（与上模组料号同前缀的 D 后缀图号），如 51661214V000D01</summary>
        public string UpAssemDraw { get; set; }

        /// <summary>上模组组装图2（其余 D 后缀图号），如 51661217V000D01</summary>
        public string UpAssemDraw2 { get; set; }
    }

    /// <summary>
    /// 吉茂-梯级（销售订单明细行）
    /// 聚合条件：同一 Sales order ref.（斜杠前纯数字）的 3 个组件行（主轴/上模组/下模组）。
    /// 物料 = 梯号（成品，我们建）；组件只引用客户已建物料。
    /// </summary>
    public class JmPdfTier
    {
        /// <summary>梯号（Sales order ref. 斜杠前纯数字串，成品物料编码），如 36151677</summary>
        public string TierNo { get; set; }

        /// <summary>主轴组件行</summary>
        public JmPdfComponentRow Axis { get; set; }

        /// <summary>上模组组件行</summary>
        public JmPdfComponentRow UpModule { get; set; }

        /// <summary>下模组组件行</summary>
        public JmPdfComponentRow DownModule { get; set; }

        /// <summary>
        /// 梯级单价（含税） = 主轴金额 + 上模组金额 + 下模组金额。
        /// 样例：63.82 + 10,298.57 + 11,776.74 = 22,139.13；×4 = 88,556.52 = PDF 总额
        /// </summary>
        public decimal UnitPrice
        {
            get
            {
                decimal total = 0m;
                if (Axis != null) total += Axis.Amount;
                if (UpModule != null) total += UpModule.Amount;
                if (DownModule != null) total += DownModule.Amount;
                return total;
            }
        }
    }

    /// <summary>
    /// 吉茂-组件行（PDF 表格一行）
    /// 逐行结构：Pos → Material → Arr.date → Qty → PC → Price → Amount → [techBefore] → Sales order ref. → Project ref. → [工艺值区] → REV#
    /// </summary>
    public class JmPdfComponentRow
    {
        /// <summary>行号（Pos），如 10</summary>
        public string Pos { get; set; }

        /// <summary>组件图号（Material），如 KM52209123V003</summary>
        public string Material { get; set; }

        /// <summary>到货日期（Arr.date），如 17.08.2026</summary>
        public string ArrDate { get; set; }

        /// <summary>数量（Quantity），如 1</summary>
        public decimal Qty { get; set; }

        /// <summary>单价（Price），如 63.82</summary>
        public decimal Price { get; set; }

        /// <summary>金额（Amount），如 63.82</summary>
        public decimal Amount { get; set; }

        /// <summary>Sales order ref.（梯号/期数），如 36151677/20</summary>
        public string SalesOrderRef { get; set; }

        /// <summary>Project ref.（项目号），如 4562682661</summary>
        public string ProjectRef { get; set; }

        /// <summary>REV#（版本），如 - 或 B</summary>
        public string Rev { get; set; }

        /// <summary>工艺值区文本（Project ref. 与 REV# 之间的解码文本，上模组行才有完整工艺值）</summary>
        public string TechZone { get; set; }
    }
}
