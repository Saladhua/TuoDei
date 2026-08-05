using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core.Metadata.ConvertElement.PlugIn;
using Kingdee.BOS.Core.Metadata.ConvertElement.PlugIn.Args;
using Kingdee.BOS.Core.Metadata.EntityElement;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单下推生产订单，携带工艺要求到生产订单明细行 FMEMO 转换插件
    ///
    /// 触发：销售订单 → 生产订单（下推/转换），挂转换规则（&lt;ConvertPlugins&gt;），服务端执行。
    /// 逻辑（用户确认 2026-08-05）：
    ///   下推时，读取销售订单明细行工艺组件字段（14 个，用户确认已存在于销售订单），
    ///   仅取非空值，按 "字段名:值" 每项一行拼接为一个文本，
    ///   写入目标生产订单明细行 FMEMO 字段。
    ///
    /// 字段标识为拟定占位（F_CustLI_ 前缀，见配置区），待用户提供实际标识后替换。
    /// 数据获取：AfterConvert 中通过目标单 Link 关联收集源分录内码，SQL 批量查销售订单明细工艺字段
    ///   （避免循环内查 DB），再按源分录内码匹配写入目标明细行 FMEMO。
    /// </summary>
    [Description("吉茂-销售订单下推生产订单携带工艺要求"), HotUpdate]
    public class JmSalOrderToProdOrderConvertPlugIn : AbstractConvertPlugIn
    {
        // ==================== 配置区（字段标识为拟定占位，待用户提供实际值后替换） ====================

        /// <summary>目标生产订单明细行备注字段标识（用户确认 FMEMO，待演示环境核验）</summary>
        private const string TargetMemoFieldKey = "FMEMO";

        /// <summary>工艺组件字段清单：[字段标识, 中文名]，仅非空值参与拼接</summary>
        private static readonly string[,] TechFieldMap = new string[,]
        {
            { "F_CustLI_UpperPart", "上部" },
            { "F_CustLI_3DModule", "3D模组" },
            { "F_CustLI_Chain", "链条" },
            { "F_CustLI_AxisAssy", "主轴组件" },
            { "F_CustLI_Axis", "主轴" },
            { "F_CustLI_StepSprocket", "梯级链轮" },
            { "F_CustLI_StepSprocketNonStd", "梯级链轮(非标)" },
            { "F_CustLI_DriveSprocket", "驱动链轮" },
            { "F_CustLI_DriveSprocketNonStd", "驱动链轮(非标)" },
            { "F_CustLI_HandrailAssy", "扶手轴组件" },
            { "F_CustLI_Shaft", "轴" },
            { "F_CustLI_Brake", "制动器" },
            { "F_CustLI_BrakeRod", "抱闸拉杆" },
            { "F_CustLI_LowerPart", "下部" }
        };

        /// <summary>
        /// 转换完成后，将销售订单明细工艺组件字段汇总写入生产订单明细行 FMEMO。
        /// </summary>
        /// <param name="e">转换事件参数</param>
        public override void AfterConvert(AfterConvertEventArgs e)
        {
            base.AfterConvert(e);

            // 目标单未设置关联主实体，无法反查源单明细，直接返回
            var targetForm = e.TargetBusinessInfo.GetForm();
            if (targetForm.LinkSet == null
                || targetForm.LinkSet.LinkEntitys == null
                || targetForm.LinkSet.LinkEntitys.Count == 0)
            {
                return;
            }

            // 关联主实体（目标单明细实体）与 Link 子实体
            Entity entity = e.TargetBusinessInfo.GetEntity(
                targetForm.LinkSet.LinkEntitys[0].ParentEntityKey);
            Entity linkEntity = e.TargetBusinessInfo.GetEntity(
                targetForm.LinkSet.LinkEntitys[0].Key);

            // 目标行索引 → 源分录内码集合；并收集全部源分录内码
            Dictionary<int, HashSet<long>> dctIndexToSrcSId = new Dictionary<int, HashSet<long>>();
            HashSet<long> srcSIds = new HashSet<long>();

            var entryRows = e.Result.FindByEntityKey(entity.Key);
            int dataIndex = 0;
            foreach (var entryRow in entryRows)
            {
                if (!dctIndexToSrcSId.ContainsKey(dataIndex))
                {
                    dctIndexToSrcSId.Add(dataIndex, new HashSet<long>());
                }

                var linkRows = linkEntity.DynamicProperty.GetValue(entryRow.DataEntity) as DynamicObjectCollection;
                if (linkRows != null)
                {
                    foreach (var linkRow in linkRows)
                    {
                        long srcSId = Convert.ToInt64(linkRow["SId"]);
                        if (srcSIds.Contains(srcSId) == false)
                        {
                            srcSIds.Add(srcSId);
                        }
                        if (dctIndexToSrcSId[dataIndex].Contains(srcSId) == false)
                        {
                            dctIndexToSrcSId[dataIndex].Add(srcSId);
                        }
                    }
                }
                dataIndex++;
            }
            if (srcSIds.Count == 0) return;

            // SQL 批量查源销售订单明细工艺字段（源分录内码集合）
            Dictionary<long, string> dctSrcSIdToMemo = QuerySrcTechMemo(e, srcSIds);

            // 遍历目标行，按源分录内码匹配写入 FMEMO
            foreach (var item in dctIndexToSrcSId)
            {
                DynamicObject targetEntry = entryRows[item.Key].DataEntity;
                StringBuilder sb = new StringBuilder();
                foreach (long srcSId in item.Value)
                {
                    string memo;
                    if (dctSrcSIdToMemo.TryGetValue(srcSId, out memo))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(memo);
                    }
                }
                if (sb.Length > 0)
                {
                    targetEntry[TargetMemoFieldKey] = sb.ToString();
                }
            }
        }

        /// <summary>
        /// SQL 批量查源销售订单明细工艺字段，按源分录内码返回已拼接文本。
        /// </summary>
        /// <param name="e">转换事件参数（取源表单标识）</param>
        /// <param name="srcSIds">源分录内码集合</param>
        /// <returns>源分录内码 → 工艺字段汇总文本</returns>
        private Dictionary<long, string> QuerySrcTechMemo(AfterConvertEventArgs e, HashSet<long> srcSIds)
        {
            Dictionary<long, string> map = new Dictionary<long, string>();
            if (srcSIds.Count == 0) return map;

            // 拼 SELECT 字段列表（源明细表别名 a1）
            StringBuilder selectFields = new StringBuilder();
            for (int i = 0; i < TechFieldMap.GetLength(0); i++)
            {
                if (i > 0) selectFields.Append(",");
                selectFields.Append("a1.").Append(TechFieldMap[i, 0]);
            }

            string ids = string.Join(",", srcSIds.ToArray());
            string sql = string.Format(
                @"SELECT a1.FENTRYID AS FENTRYID,{0}
                  FROM T_SAL_ORDERENTRY a1
                  WHERE a1.FENTRYID IN ({1})",
                selectFields, ids);

            var dbService = ServiceFactory.GetDBService(Context);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(Context, sql);
            if (rows == null || rows.Count == 0) return map;

            foreach (DynamicObject row in rows)
            {
                long entryId = Convert.ToInt64(row["FENTRYID"]);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < TechFieldMap.GetLength(0); i++)
                {
                    string fieldKey = TechFieldMap[i, 0];
                    string name = TechFieldMap[i, 1];
                    string value = ObjectToString(row[fieldKey]);
                    if (string.IsNullOrEmpty(value)) continue;

                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(name).Append(":").Append(value);
                }
                if (sb.Length > 0)
                {
                    map[entryId] = sb.ToString();
                }
            }

            return map;
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