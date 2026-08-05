using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单审核时生成物料清单（BOM）操作插件
    ///
    /// 触发时机（用户确认 2026-08-05）：销售订单（SAL_SaleOrder）审核（Audit）操作。
    /// 逻辑：在 EndOperationTransaction（事务内，核心审核已完成）遍历审核单据明细，
    ///   按每梯号生成 1 个大 BOM（父=梯号，子=主轴/上模组/下模组，用量 1/1）；
    ///   父项梯号已有非作废 BOM 时跳过（JmBomGenerateHelper.BomExists），不阻断审核。
    ///
    /// 数据来源：销售订单明细行梯号 FMaterialId + 组件 F_CustLI_AxisCode/UpCode/DownCode
    ///   （JmSalOrderSaveHelper.SetComponentFields 已写入）。
    /// 性能：循环外一次性批量查料号→物料内码（JmMaterialHelper.BatchQueryMaterialIds，避免循环内查 DB）。
    /// </summary>
    [Description("吉茂-销售订单审核时生成物料清单(BOM)"), HotUpdate]
    public class JmSalOrderAuditCreateBomPlugIn : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FMaterialId");
            e.FieldKeys.Add("F_CustLI_AxisCode");
            e.FieldKeys.Add("F_CustLI_UpCode");
            e.FieldKeys.Add("F_CustLI_DownCode");
        }

        public override void EndOperationTransaction(EndOperationTransactionArgs e)
        {
            base.EndOperationTransaction(e);

            // 收集每梯号的组件料号（主轴/上模组/下模组）
            Dictionary<string, List<string>> tierChildren = new Dictionary<string, List<string>>();
            if (e.DataEntitys != null)
            {
                foreach (DynamicObject billObj in e.DataEntitys)
                {
                    if (billObj == null) continue;

                    DynamicObjectCollection entryCol =
                        billObj[JmSalOrderSaveHelper.SaleOrderEntryCollectionKey] as DynamicObjectCollection;
                    if (entryCol == null) continue;

                    foreach (DynamicObject entry in entryCol)
                    {
                        string tierNo = GetMaterialNumber(entry["MaterialId"]);
                        if (string.IsNullOrEmpty(tierNo)) continue;

                        if (!tierChildren.ContainsKey(tierNo)) tierChildren[tierNo] = new List<string>();
                        AddChild(tierChildren[tierNo], ObjectToString(entry["F_CustLI_AxisCode"]));
                        AddChild(tierChildren[tierNo], ObjectToString(entry["F_CustLI_UpCode"]));
                        AddChild(tierChildren[tierNo], ObjectToString(entry["F_CustLI_DownCode"]));
                    }
                }
            }
            if (tierChildren.Count == 0) return;

            // 一次性批量查全部料号（梯号 + 组件）→ 物料内码（避免循环内查 DB）
            List<string> allNumbers = new List<string>();
            foreach (KeyValuePair<string, List<string>> kvp in tierChildren)
            {
                allNumbers.Add(kvp.Key);
                allNumbers.AddRange(kvp.Value);
            }
            Dictionary<string, long> matMap = JmMaterialHelper.BatchQueryMaterialIds(Context, allNumbers);

            // 逐梯号生成 BOM（已存在跳过）
            foreach (KeyValuePair<string, List<string>> kvp in tierChildren)
            {
                long parentId;
                if (!matMap.TryGetValue(kvp.Key, out parentId) || parentId <= 0) continue;
                if (JmBomGenerateHelper.BomExists(Context, parentId)) continue;

                List<long> childIds = new List<long>();
                foreach (string child in kvp.Value)
                {
                    long childId;
                    if (matMap.TryGetValue(child, out childId) && childId > 0 && !childIds.Contains(childId))
                    {
                        childIds.Add(childId);
                    }
                }
                if (childIds.Count == 0) continue;

                JmBomGenerateHelper.CreateBom(Context, parentId, childIds);
            }
        }

        /// <summary>
        /// 取物料引用字段的编码（FMaterialId 为基出引用，取 Number）。
        /// </summary>
        /// <param name="matObj">物料字段值</param>
        /// <returns>物料编码；无返回空串</returns>
        private static string GetMaterialNumber(object matObj)
        {
            if (matObj == null) return "";
            DynamicObject mat = matObj as DynamicObject;
            if (mat != null) return ObjectToString(mat["Number"]);
            return ObjectToString(matObj);
        }

        /// <summary>
        /// 收集组件料号（销售订单主轴/上模组/下模组字段值即子项料号；过滤空；去重）。
        /// </summary>
        /// <param name="list">目标集合</param>
        /// <param name="code">组件料号</param>
        private static void AddChild(List<string> list, string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            if (!list.Contains(code)) list.Add(code);
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