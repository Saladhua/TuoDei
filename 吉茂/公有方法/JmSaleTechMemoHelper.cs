using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单工艺要求汇总查询帮助类
    ///
    /// 统一"工艺要求汇总文本"的生成规则，供两个入口共用，保证结果一致：
    ///   - ④ 销售订单下推生产订单（JmSalOrderToProdOrderConvertPlugIn，按源分录内码查）
    ///   - 生产订单保存自动填充（JmPrdOrderFillBomSaveServicePlugIn，按销售订单头内码反查全部分录）
    ///
    /// 拼接规则（与 JmSalOrderToProdOrderConvertPlugIn 保持相同）：
    ///   工艺字段清单 TechFieldMap 的每个字段，仅 RegisteredTechColumns 白名单中真实已注册列，
    ///   且非空值参与拼接，按 "中文名:值" 每项一行。
    ///
    /// 白名单说明：T_SAL_ORDERENTRY 实测（2026-08-05）14 个工艺占位字段中，仅
    ///   主轴 F_CUSTLI_AXISCODE / 下部 F_CUSTLI_DOWNCODE 真实已注册可参与查询与拼接；
    ///   其余 12 个保留占位但值为空，不参与 SQL 与拼接。
    /// </summary>
    public static class JmSaleTechMemoHelper
    {
        // ==================== 配置区（与 JmSalOrderToProdOrderConvertPlugIn 一致） ====================

        /// <summary>工艺字段清单：[字段标识, 中文名]，仅已注册列（RegisteredTechColumns）且非空值参与拼接</summary>
        /// 主轴/下部 映射到销售订单已存在字段（F_CUSTLI_AXISCODE 主轴图号 / F_CUSTLI_DOWNCODE 下模组料号）；
        /// 其余 12 个（上部/3D模组/链条/主轴组件/梯级链轮×2/驱动链轮×2/扶手轴组件/轴/制动器/抱闸拉杆）
        /// 在 T_SAL_ORDERENTRY 无对应列（实测 2026-08-05），保留占位但值为空，不参与 SQL 与拼接。
        public static readonly string[,] TechFieldMap = new string[,]
        {
            { "F_CustLI_UpperPart", "上部" },
            { "F_CustLI_3DModule", "3D模组" },
            { "F_CustLI_Chain", "链条" },
            { "F_CustLI_AxisAssy", "主轴组件" },
            { "F_CUSTLI_AXISCODE", "主轴" },
            { "F_CustLI_StepSprocket", "梯级链轮" },
            { "F_CustLI_StepSprocketNonStd", "梯级链轮(非标)" },
            { "F_CustLI_DriveSprocket", "驱动链轮" },
            { "F_CustLI_DriveSprocketNonStd", "驱动链轮(非标)" },
            { "F_CustLI_HandrailAssy", "扶手轴组件" },
            { "F_CustLI_Shaft", "轴" },
            { "F_CustLI_Brake", "制动器" },
            { "F_CustLI_BrakeRod", "抱闸拉杆" },
            { "F_CUSTLI_DOWNCODE", "下部" }
        };

        /// <summary>T_SAL_ORDERENTRY 中真实已注册的工艺列标识白名单，仅白名单列参与 SQL SELECT 与取值</summary>
        public static readonly HashSet<string> RegisteredTechColumns = new HashSet<string>
        {
            "F_CUSTLI_AXISCODE",
            "F_CUSTLI_DOWNCODE"
        };

        /// <summary>
        /// 按源销售订单分录内码集合批量查工艺字段，返回 分录内码 → 该分录工艺汇总文本。
        /// （供下推转换插件使用：2026-08-05 实测 T_SAL_ORDERENTRY 仅白名单列可查。）
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="srcEntryIds">源销售订单分录内码集合</param>
        /// <returns>分录内码 → 工艺字段汇总文本</returns>
        public static Dictionary<long, string> GetSrcEntryTechMemo(Context ctx, HashSet<long> srcEntryIds)
        {
            Dictionary<long, string> map = new Dictionary<long, string>();
            if (srcEntryIds == null || srcEntryIds.Count == 0) return map;

            DynamicObjectCollection rows = QueryTechMemo(ctx, "FENTRYID", srcEntryIds);
            if (rows == null || rows.Count == 0) return map;

            foreach (DynamicObject row in rows)
            {
                long entryId = Convert.ToInt64(row["FENTRYID"]);
                string text = BuildMemo(row);
                if (text.Length > 0) map[entryId] = text;
            }
            return map;
        }

        /// <summary>
        /// 按源销售订单头内码集合批量查工艺字段，汇总每个销售订单全部分录的工艺文本。
        /// 返回 销售订单头内码 → 该单全部分录工艺文本（换行连接）。
        /// 供生产订单保存插件使用：生产订单明细 FSALEORDERID 指向销售订单头内码 FID。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="saleOrderFids">销售订单头内码集合</param>
        /// <returns>销售订单头内码 → 该单工艺字段汇总文本</returns>
        public static Dictionary<long, string> GetSaleOrderTechMemo(Context ctx, HashSet<long> saleOrderFids)
        {
            Dictionary<long, string> map = new Dictionary<long, string>();
            if (saleOrderFids == null || saleOrderFids.Count == 0) return map;

            DynamicObjectCollection rows = QueryTechMemo(ctx, "FID", saleOrderFids);
            if (rows == null || rows.Count == 0) return map;

            // 按销售订单头内码分组汇总，保证每个订单结果为一个文本
            Dictionary<long, StringBuilder> tmp = new Dictionary<long, StringBuilder>();
            foreach (DynamicObject row in rows)
            {
                long fid = Convert.ToInt64(row["FID"]);
                string text = BuildMemo(row);
                if (text.Length == 0) continue;

                StringBuilder sb;
                if (!tmp.TryGetValue(fid, out sb))
                {
                    sb = new StringBuilder();
                    tmp[fid] = sb;
                }
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(text);
            }

            foreach (KeyValuePair<long, StringBuilder> pair in tmp)
            {
                map[pair.Key] = pair.Value.ToString();
            }
            return map;
        }

        /// <summary>
        /// SQL 批量查销售订单明细工艺字段行（按 whereField 匹配主键集合）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="whereField">WHERE 匹配字段：FENTRYID（分录内码）或 FID（销售订单头内码）</param>
        /// <param name="ids">匹配主键集合</param>
        /// <returns>查询结果行集合</returns>
        private static DynamicObjectCollection QueryTechMemo(Context ctx, string whereField, HashSet<long> ids)
        {
            StringBuilder selectFields = new StringBuilder();
            bool hasSelect = false;
            for (int i = 0; i < TechFieldMap.GetLength(0); i++)
            {
                string fieldKey = TechFieldMap[i, 0];
                if (!RegisteredTechColumns.Contains(fieldKey)) continue;
                if (hasSelect) selectFields.Append(",");
                selectFields.Append("a1.").Append(fieldKey);
                hasSelect = true;
            }
            if (!hasSelect) return null;

            string idsText = string.Join(",", ids.ToArray());
            string sql = string.Format(
                @"SELECT a1.FID AS FID, a1.FENTRYID AS FENTRYID,{0}
                  FROM T_SAL_ORDERENTRY a1
                  WHERE a1.{1} IN ({2})",
                selectFields, whereField, idsText);

            var dbService = ServiceFactory.GetDBService(ctx);
            return dbService.ExecuteDynamicObject(ctx, sql);
        }

        /// <summary>
        /// 拼接单条销售订单明细行的工艺字段文本（仅白名单且非空值参与，按 "中文名:值" 一行）。
        /// </summary>
        /// <param name="row">销售订单明细行查询结果</param>
        /// <returns>工艺字段汇总文本；无有效值返回空串</returns>
        private static string BuildMemo(DynamicObject row)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < TechFieldMap.GetLength(0); i++)
            {
                string fieldKey = TechFieldMap[i, 0];
                // 仅白名单中的真实已注册列参与拼接；否则字段无值（留空不显示）
                if (!RegisteredTechColumns.Contains(fieldKey)) continue;
                string name = TechFieldMap[i, 1];
                string value = ObjectToString(row[fieldKey]);
                if (string.IsNullOrEmpty(value)) continue;

                if (sb.Length > 0) sb.AppendLine();
                sb.Append(name).Append(":").Append(value);
            }
            return sb.ToString();
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
}