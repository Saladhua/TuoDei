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
    /// 吉茂-销售订单下推生产订单，携带工艺要求到生产订单表头 FDescription（备注）转换插件
    ///
    /// 触发：销售订单 → 生产订单（下推/转换），挂转换规则（&lt;ConvertPlugins&gt;），服务端执行。
    /// 逻辑（用户确认 2026-08-07）：
    ///   下推时，读取销售订单明细行工艺组件字段（14 个，用户确认已存在于销售订单），
    ///   仅取非空值，按 "字段名:值" 每项一行拼接为一个文本，
    ///   汇总写入目标生产订单表头备注字段 FDescription（一个单据只有一条数据，落点由明细行 MEMO 改为表头）。
    ///
    /// 字段标识为拟定占位（F_CustLI_ 前缀，见配置区），待用户提供实际标识后替换。
    /// 数据获取：AfterConvert 中通过目标单 Link 关联收集源分录内码，SQL 批量查销售订单明细工艺字段
    ///   （避免循环内查 DB），再按源分录内码匹配汇总为一个文本写入目标表头备注字段。
    /// </summary>
    [Description("吉茂-销售订单下推生产订单携带工艺要求"), HotUpdate]
    public class JmSalOrderToProdOrderConvertPlugIn : AbstractConvertPlugIn
    {
        /// <summary>目标生产订单表头备注字段（标准字段 FDescription，ORM 属性名去 F = Description，用户确认 2026-08-07）</summary>
        private const string TargetDescriptionFieldKey = "Description";

        /// <summary>
        /// 转换完成后，将销售订单明细工艺组件字段汇总写入生产订单表头备注字段。
        /// 一个单据只有一条数据：表头只写一个整体汇总文本（不再逐明细行写 MEMO，用户确认 2026-08-07）。
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

            // 收集全部源分录内码（关联主实体下逐条明细行的 Link 子实体）
            HashSet<long> srcSIds = new HashSet<long>();

            var entryRows = e.Result.FindByEntityKey(entity.Key);
            foreach (var entryRow in entryRows)
            {
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
                    }
                }
            }
            if (srcSIds.Count == 0) return;

            // SQL 批量查源销售订单明细工艺字段（源分录内码集合）
            Dictionary<long, string> dctSrcSIdToMemo = JmSaleTechMemoHelper.GetSrcEntryTechMemo(Context, srcSIds);

            // 全部源分录工艺文本汇总为一个整体文本，写入目标生产订单表头备注字段
            StringBuilder sb = new StringBuilder();
            foreach (long srcSId in srcSIds)
            {
                string memo;
                if (dctSrcSIdToMemo.TryGetValue(srcSId, out memo))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(memo);
                }
            }
            if (sb.Length == 0) return;

            // 目标单表头数据（一个单据只有一条数据，取第一条；根实体 = BusinessInfo.Entrys[0]）
            if (e.TargetBusinessInfo.Entrys == null || e.TargetBusinessInfo.Entrys.Count == 0) return;
            Entity rootEntity = e.TargetBusinessInfo.Entrys[0];
            var rootRows = e.Result.FindByEntityKey(rootEntity.Key);
            if (rootRows == null || rootRows.Length == 0) return;
            DynamicObject targetBill = rootRows[0].DataEntity;
            targetBill[TargetDescriptionFieldKey] = sb.ToString();
        }
    }
}