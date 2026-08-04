using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单导入 · 采购合同PDF关键字驱动解析器
    ///
    /// 解析流程：JmPdfTextExtractor 提取解码片段 → 拼接全文 → 关键字正则抽取：
    ///   1. 单据头：Purchase order No.（订单号）、Date（订单日期）、CURRENCY 区（币别/总额）、Terms of delivery（交货条款）
    ///   2. 组件行：逐行匹配（Pos→Material→Arr.date→Qty→PC→Price→Amount→Sales order ref.→Project ref.→工艺值区→REV#）
    ///   3. 工艺字段（仅特征明确值）：防腐等级（C4-High 模式）、上模组组装图1/2（与上模组料号同前缀的 D 后缀图号）
    ///   4. 梯级聚合：按 Sales order ref. 斜杠前数字分组，组内按配置区图号前缀识别主轴/上模组/下模组
    ///
    /// 多版本适配：关键字/图号前缀收敛在解析配置区，新增版本只补配置不重写逻辑。
    /// 缺字段留空不提示；仅梯级组件行缺失（解析不到 3 个组件）报错该份 PDF。
    /// </summary>
    public static class JmPdfParseHelper
    {
        // ==================== 解析配置区（多版本 PDF 新增只需调整此处） ====================

        /// <summary>主轴组件图号前缀（客户已建物料，缺失报错中止该份 PDF）</summary>
        public const string AxisMaterialPrefix = "KM52209123";

        /// <summary>上模组组件图号前缀（客户已建物料，缺失报错中止该份 PDF）</summary>
        public const string UpModuleMaterialPrefix = "KM51661214";

        /// <summary>下模组组件图号前缀（客户已建物料，缺失报错中止该份 PDF）</summary>
        public const string DownModuleMaterialPrefix = "KM52340306";

        /// <summary>主轴组件角色标识</summary>
        public const string RoleAxis = "Axis";

        /// <summary>上模组组件角色标识</summary>
        public const string RoleUpModule = "UpModule";

        /// <summary>下模组组件角色标识</summary>
        public const string RoleDownModule = "DownModule";

        // ==================== 关键字/正则（多版本关键字补充处） ====================

        /// <summary>订单号：Purchase order No. 4800761812</summary>
        private static readonly Regex BillNoPattern =
            new Regex(@"Purchase\s+order\s+No\.?\s*[:]?\s*(\d+)", RegexOptions.IgnoreCase);

        /// <summary>通力英文买方名称：PDF 只能抽到英文时，归一为金蝶客户中文名称。</summary>
        private static readonly Regex KoneCustomerPattern =
            new Regex(@"KONE\s+Elevators\s+Co\.?\s*,?\s*Ltd\.?", RegexOptions.IgnoreCase);

        /// <summary>客户名称：Buyer 后的中文客户名称（英文买方走配置化归一，避免 Buyer VAT No 误识别）。</summary>
        private static readonly Regex CustomerPattern =
            new Regex(@"Buyer\s*[:]?\s*([\u4e00-\u9fa5（）()]{2,60})", RegexOptions.IgnoreCase);

        /// <summary>通力在金蝶客户基础资料中的中文名称。</summary>
        private const string KoneCustomerName = "通力电梯有限公司";

        /// <summary>订单日期：Date 20.07.2026（取第一个 Date 后的日期）</summary>
        private static readonly Regex OrderDatePattern =
            new Regex(@"\bDate\s*[:]?\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase);

        /// <summary>币别+合同总额：CURRENCY TOTAL AMOUNT 区后的币别（3 位字母）与金额（含千分位）</summary>
        private static readonly Regex CurrencyTotalPattern =
            new Regex(@"CURRENCY\s+TOTAL\s+AMOUNT\s+.*?\s*([A-Z]{3})\s+([\d,\.]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>交货条款：Terms of delivery 后的英文短语</summary>
        private static readonly Regex DeliveryTermPattern =
            new Regex(@"Terms\s+of\s+delivery\s*[:]?\s*([A-Za-z\.\s]{3,60})", RegexOptions.IgnoreCase);

        /// <summary>防腐等级（特征明确值）：C4-High / C5-Moderate 等</summary>
        private static readonly Regex AntiGradePattern = new Regex(@"\bC\d+\-\w+\b");

        /// <summary>组装图类图号（无 KM 前缀的 D 后缀图号）：51661214V000D01</summary>
        private static readonly Regex AssemDrawPattern = new Regex(@"\d{8}V\d{3}D\d{2}");

        /// <summary>组件行主体：Pos→Material→Arr.date→Qty→PC→Price→Amount→Sales order ref.→Project ref.</summary>
        private static readonly Regex ComponentRowPattern = new Regex(
            @"(?<pos>\d{2,3})\s+(?<mat>[A-Za-z0-9]+)\s+(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<qty>\d+)\s+PC\s+(?<price>[\d,\.]+)\s+(?<amount>[\d,\.]+)\s+(?<tech>.*?)\s+Sales\s+order\s+ref\.\s+(?<ref>\d+/\d+)\s+Project\s+ref\.\s+(?<proj>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>REV# 版本：REV#: B / REV#: -</summary>
        private static readonly Regex RevPattern = new Regex(@"REV#:?\s*([^\s]+)", RegexOptions.Singleline);

        /// <summary>从组件图号中取数字前缀（如 KM51661214V000 → 51661214）</summary>
        private static readonly Regex NumberPrefixPattern = new Regex(@"(\d+)");

        // ==================== 解析入口 ====================

        /// <summary>
        /// 解析一份 PDF 采购合同。
        /// </summary>
        /// <param name="pdfBytes">PDF 文件字节</param>
        /// <returns>解析结果（Success=false 时 ErrorMessage 为原因）</returns>
        public static JmPdfOrder Parse(byte[] pdfBytes)
        {
            JmPdfOrder order = new JmPdfOrder();
            try
            {
                List<string> fragments = JmPdfTextExtractor.ExtractFragments(pdfBytes);
                if (fragments == null || fragments.Count == 0)
                {
                    order.Success = false;
                    order.ErrorMessage = "PDF 文本提取为空，无法解析";
                    return order;
                }

                string fullText = string.Join(" ", fragments);

                // 1. 单据头
                ParseHead(order.Head, fullText);

                // 2. 组件行（含工艺值区）
                List<JmPdfComponentRow> rows = ParseComponentRows(fullText);
                if (rows == null || rows.Count == 0)
                {
                    order.Success = false;
                    order.ErrorMessage = "未解析到任何组件行（图号/表格结构不识别）";
                    return order;
                }

                // 3. 梯级聚合（按 Sales order ref. 前缀分组 + 配置区图号前缀识别角色）
                order.Tiers = BuildTiers(rows);
                if (order.Tiers == null || order.Tiers.Count == 0)
                {
                    order.Success = false;
                    order.ErrorMessage = "梯级聚合失败（组件行无法按梯号/角色归类）";
                    return order;
                }

                // 4. 工艺字段（特征明确值）
                ParseTechFields(order.Head, order.Tiers, fullText);

                order.Success = true;
                return order;
            }
            catch (Exception ex)
            {
                order.Success = false;
                order.ErrorMessage = "PDF 解析异常：" + ex.Message;
                return order;
            }
        }

        // ==================== 单据头解析 ====================

        /// <summary>
        /// 解析单据头关键字字段；取不到的值留空（缺字段留空不提示，用户确认）。
        /// </summary>
        /// <param name="head">单据头模型</param>
        /// <param name="fullText">全文</param>
        private static void ParseHead(JmPdfHead head, string fullText)
        {
            Match m = BillNoPattern.Match(fullText);
            if (m.Success) head.BillNo = m.Groups[1].Value;

            head.Customer = ParseCustomerName(fullText);

            m = OrderDatePattern.Match(fullText);
            if (m.Success) head.OrderDate = m.Groups[1].Value;

            m = CurrencyTotalPattern.Match(fullText);
            if (m.Success)
            {
                head.Currency = m.Groups[1].Value;
                head.CurrencyTotal = m.Groups[2].Value;
            }

            m = DeliveryTermPattern.Match(fullText);
            if (m.Success)
            {
                head.DeliveryTerm = m.Groups[1].Value.Trim();
            }
        }

        /// <summary>
        /// 解析客户名称；中文 Buyer 名称优先，英文 KONE 买方名称按金蝶客户 FNAME 归一。
        /// </summary>
        /// <param name="fullText">全文</param>
        /// <returns>客户名称；取不到返回空串</returns>
        private static string ParseCustomerName(string fullText)
        {
            Match m = CustomerPattern.Match(fullText);
            if (m.Success) return m.Groups[1].Value.Trim();

            // 样例 PDF 的 Buyer 后先出现 VAT No，客户英文名出现在 Delivery address 区域。
            m = KoneCustomerPattern.Match(fullText);
            if (m.Success) return KoneCustomerName;

            return "";
        }

        // ==================== 组件行解析 ====================

        /// <summary>
        /// 逐行匹配组件行，行主体匹配后从匹配位置向后取 REV#，两者之间为工艺值区。
        /// </summary>
        /// <param name="fullText">全文</param>
        /// <returns>组件行列表</returns>
        private static List<JmPdfComponentRow> ParseComponentRows(string fullText)
        {
            List<JmPdfComponentRow> rows = new List<JmPdfComponentRow>();

            int startIndex = 0;
            Match m = ComponentRowPattern.Match(fullText, startIndex);
            while (m.Success)
            {
                int afterProject = m.Index + m.Length;
                string techZone = "";
                string rev = "";

                Match rm = RevPattern.Match(fullText, afterProject);
                if (rm.Success)
                {
                    rev = rm.Groups[1].Value;
                    // 工艺值区 = Project ref. 结束位置 到 REV# 起始位置之间的文本
                    techZone = fullText.Substring(afterProject, rm.Index - afterProject);
                }

                JmPdfComponentRow row = new JmPdfComponentRow
                {
                    Pos = m.Groups["pos"].Value,
                    Material = m.Groups["mat"].Value,
                    ArrDate = m.Groups["date"].Value,
                    Qty = ParseDecimal(m.Groups["qty"].Value),
                    Price = ParseDecimal(m.Groups["price"].Value),
                    Amount = ParseDecimal(m.Groups["amount"].Value),
                    SalesOrderRef = m.Groups["ref"].Value,
                    ProjectRef = m.Groups["proj"].Value,
                    Rev = rev,
                    TechZone = techZone
                };
                rows.Add(row);

                // 从当前行结束位置继续找下一行
                startIndex = m.Index + m.Length;
                m = ComponentRowPattern.Match(fullText, startIndex);
            }
            return rows;
        }

        /// <summary>
        /// 金额解析：支持千分位（10,298.57）与纯数字。
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <returns>解析后的数值；解析失败返回 0</returns>
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

        // ==================== 梯级聚合 ====================

        /// <summary>
        /// 按 Sales order ref.（斜杠前纯数字）分组组件行，组内按图号前缀识别主轴/上模组/下模组。
        /// 同一角色多行（如数量大于 1）时数量/金额累加，图号/版本取第一行。
        /// 组内任一角色缺失 → 该份 PDF 报错（组件行不完整，禁止生成缺组件的销售订单）。
        /// </summary>
        /// <param name="rows">组件行列表</param>
        /// <returns>梯级列表；识别失败时抛异常由 Parse 捕获</returns>
        private static List<JmPdfTier> BuildTiers(List<JmPdfComponentRow> rows)
        {
            // 梯号 -> 行列表
            Dictionary<string, List<JmPdfComponentRow>> groupMap = new Dictionary<string, List<JmPdfComponentRow>>();
            foreach (JmPdfComponentRow row in rows)
            {
                string tierNo = ExtractTierNo(row.SalesOrderRef);
                if (string.IsNullOrEmpty(tierNo)) continue;

                List<JmPdfComponentRow> group;
                if (!groupMap.TryGetValue(tierNo, out group))
                {
                    group = new List<JmPdfComponentRow>();
                    groupMap[tierNo] = group;
                }
                group.Add(row);
            }

            List<JmPdfTier> tiers = new List<JmPdfTier>();
            foreach (KeyValuePair<string, List<JmPdfComponentRow>> kvp in groupMap)
            {
                string tierNo = kvp.Key;
                List<JmPdfComponentRow> group = kvp.Value;

                JmPdfTier tier = new JmPdfTier { TierNo = tierNo };

                // 按角色分组（组内同角色可能多行，数量/金额累加）
                foreach (JmPdfComponentRow row in group)
                {
                    string role = MatchRole(row.Material);
                    if (role == RoleAxis) tier.Axis = MergeRow(tier.Axis, row);
                    else if (role == RoleUpModule) tier.UpModule = MergeRow(tier.UpModule, row);
                    else if (role == RoleDownModule) tier.DownModule = MergeRow(tier.DownModule, row);
                    // 角色无法识别（图号不在配置区前缀）→ 忽略，交由下方完整性校验报错
                }

                if (tier.Axis == null || tier.UpModule == null || tier.DownModule == null)
                {
                    throw new Exception(string.Format(
                        "梯级 {0} 组件行不完整（缺少 主轴/上模组/下模组 任一），图号未在组件配置区识别，请补充 JmPdfParseHelper 配置区图号前缀",
                        tierNo));
                }

                tiers.Add(tier);
            }
            return tiers;
        }

        /// <summary>
        /// 合并同角色多行：数量/金额累加，图号/版本/到货日期取第一行。
        /// </summary>
        /// <param name="existing">已合并行（可能为 null）</param>
        /// <param name="row">新行</param>
        /// <returns>合并后行</returns>
        private static JmPdfComponentRow MergeRow(JmPdfComponentRow existing, JmPdfComponentRow row)
        {
            if (existing == null)
            {
                return new JmPdfComponentRow
                {
                    Pos = row.Pos,
                    Material = row.Material,
                    ArrDate = row.ArrDate,
                    Qty = row.Qty,
                    Price = row.Price,
                    Amount = row.Amount,
                    SalesOrderRef = row.SalesOrderRef,
                    ProjectRef = row.ProjectRef,
                    Rev = row.Rev,
                    TechZone = row.TechZone
                };
            }
            existing.Qty += row.Qty;
            existing.Amount += row.Amount;
            return existing;
        }

        /// <summary>
        /// 从 Sales order ref.（如 36151677/20）取斜杠前纯数字串作为梯号。
        /// </summary>
        /// <param name="salesOrderRef">Sales order ref. 原始值</param>
        /// <returns>梯号；无法识别返回空串</returns>
        private static string ExtractTierNo(string salesOrderRef)
        {
            if (string.IsNullOrEmpty(salesOrderRef)) return "";
            int slashIndex = salesOrderRef.IndexOf('/');
            string tierNo = slashIndex > 0 ? salesOrderRef.Substring(0, slashIndex) : salesOrderRef;
            return tierNo;
        }

        /// <summary>
        /// 按配置区图号前缀识别组件角色。
        /// </summary>
        /// <param name="material">组件图号</param>
        /// <returns>角色标识（RoleAxis/RoleUpModule/RoleDownModule）；无法识别返回空串</returns>
        private static string MatchRole(string material)
        {
            if (string.IsNullOrEmpty(material)) return "";
            if (material.StartsWith(AxisMaterialPrefix)) return RoleAxis;
            if (material.StartsWith(UpModuleMaterialPrefix)) return RoleUpModule;
            if (material.StartsWith(DownModuleMaterialPrefix)) return RoleDownModule;
            return "";
        }

        // ==================== 工艺字段提取（仅特征明确值） ====================

        /// <summary>
        /// 提取工艺字段：
        ///   - 防腐等级：从上模组工艺值区提取 C4-High 模式（取不到再从全文取）
        ///   - 组装图1：D 后缀图号中与上模组料号数字前缀相同者
        ///   - 组装图2：其余 D 后缀图号（不依赖位置顺序）
        /// 其余工艺值（KM 图号 X1PC 等）特征无法唯一区分字段归属，留空（用户确认 2026-08-03）。
        /// </summary>
        /// <param name="head">单据头模型（写入工艺字段）</param>
        /// <param name="tiers">梯级列表</param>
        /// <param name="fullText">全文</param>
        private static void ParseTechFields(JmPdfHead head, List<JmPdfTier> tiers, string fullText)
        {
            // 汇总所有上模组工艺值区文本
            string upTech = "";
            string upMaterial = "";
            foreach (JmPdfTier tier in tiers)
            {
                if (tier.UpModule == null) continue;
                if (!string.IsNullOrEmpty(tier.UpModule.TechZone))
                {
                    upTech += " " + tier.UpModule.TechZone;
                }
                if (string.IsNullOrEmpty(upMaterial))
                {
                    upMaterial = tier.UpModule.Material;
                }
            }

            // 防腐等级：C4-High 模式
            Match am = AntiGradePattern.Match(upTech);
            if (!am.Success) am = AntiGradePattern.Match(fullText);
            if (am.Success) head.AntiGrade = am.Value;

            // 组装图1/2：D 后缀图号，按上模组料号数字前缀归属
            if (!string.IsNullOrEmpty(upTech))
            {
                List<string> assemDraws = new List<string>();
                foreach (Match dm in AssemDrawPattern.Matches(upTech))
                {
                    string value = dm.Value;
                    if (!assemDraws.Contains(value)) assemDraws.Add(value);
                }

                string upNumberPrefix = ExtractNumberPrefix(upMaterial);
                foreach (string value in assemDraws)
                {
                    // 与上模组料号同前缀（如 51661214）→ 组装图1；其余 → 组装图2
                    if (!string.IsNullOrEmpty(upNumberPrefix) && value.StartsWith(upNumberPrefix))
                    {
                        if (string.IsNullOrEmpty(head.UpAssemDraw)) head.UpAssemDraw = value;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(head.UpAssemDraw2)) head.UpAssemDraw2 = value;
                    }
                }
            }
        }

        /// <summary>
        /// 取组件图号的数字前缀（KM51661214V000 → 51661214）。
        /// </summary>
        /// <param name="material">组件图号</param>
        /// <returns>数字前缀；取不到返回空串</returns>
        private static string ExtractNumberPrefix(string material)
        {
            if (string.IsNullOrEmpty(material)) return "";
            Match m = NumberPrefixPattern.Match(material);
            if (m.Success) return m.Groups[1].Value;
            return "";
        }
    }
}
