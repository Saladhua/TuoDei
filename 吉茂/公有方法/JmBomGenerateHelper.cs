using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.Operation;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Web.Bill;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-BOM（物料清单）数据包构造/保存帮助类
    ///
    /// 销售订单审核时（JmSalOrderAuditCreateBomPlugIn）为每个梯号生成一个大 BOM：
    ///   父项 = 梯号（成品，审核时已存在）；
    ///   子项 = 主轴 / 上模组 / 下模组（3 个组件，用量 1/1）；
    ///   已存在策略：父项梯号已有非作废 BOM 时跳过（BomExists），不阻断审核。
    ///
    /// 数据包保存方式：CreateNewBillView + FireOnLoad + 表头/子项赋值 + Save/Submit/Audit，
    ///   复用 JmMaterialHelper 的 CreateNewBillView / SaveSubmitAudit / QueryBaseDataId（实证：林蓝数据包保存）。
    ///   引用字段统一 SetItemValueByID（金蝶内部处理 long/GUID，禁止 long.Parse 手动转换）。
    ///
    /// 演示环境待确认：BOM 单据类型编码、组织、BOM 分组/编码规则（现用占位，见配置区）。
    /// </summary>
    public static class JmBomGenerateHelper
    {
        // ==================== 配置区（演示环境需确认真实值） ====================

        /// <summary>物料清单单据标识</summary>
        public const string BomFormId = "ENG_BOM";

        /// <summary>物料清单单据类型编码（占位值 WLQD01_SYS，参考鹤见样板；演示环境待确认）</summary>
        public const string BomBillTypeNumber = "WLQD01_SYS";

        /// <summary>创建/使用组织编码（占位值 100；演示环境待确认）</summary>
        public const string OrgNumber = "100";

        /// <summary>BOM 分类（标准）</summary>
        public const string BomCategory = "标准BOM";

        /// <summary>BOM 用途（通用）</summary>
        public const string BomUse = "通用";

        /// <summary>BOM 子项实体键（CreateNewEntryRow/GetEntryRowCount 使用；BOM 树实体键为 FTreeEntity）</summary>
        public const string BomEntryKey = "FTreeEntity";

        /// <summary>
        /// 判断父项物料是否已存在有效 BOM（保存/已审核，非作废）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="parentMaterialId">父项物料（梯号）内码</param>
        /// <returns>已存在返回 true</returns>
        public static bool BomExists(Context ctx, long parentMaterialId)
        {
            string sql = string.Format(
                @"SELECT COUNT(1) AS FCOUNT
                  FROM T_ENG_BOM a1
                  WHERE a1.FMATERIALID = {0}
                    AND a1.FDOCUMENTSTATUS IN ('A','C')",
                parentMaterialId);

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows != null && rows.Count > 0 && rows[0]["FCOUNT"] != null)
            {
                return Convert.ToInt32(rows[0]["FCOUNT"]) > 0;
            }
            return false;
        }

        /// <summary>
        /// 生成并保存单个 BOM（数据包保存，保存+提交+审核）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="parentMaterialId">父项物料（梯号）内码</param>
        /// <param name="childMaterialIds">子项物料（主轴/上模组/下模组）内码集合</param>
        public static void CreateBom(Context ctx, long parentMaterialId, List<long> childMaterialIds)
        {
            IBillView view = JmMaterialHelper.CreateNewBillView(ctx, BomFormId, null);

            // 数据包保存标准模式：必须 FireOnLoad 初始化 DataObject（林蓝样板/skills create-billview-pattern 红线）
            DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
            proxy.FireOnLoad();

            // ---- 表头（基础资料引用字段统一 SetItemValueByID，普通字段 SetValue）----
            string billTypeId = JmMaterialHelper.QueryBaseDataId(ctx, "T_BAS_BILLTYPE", "FBILLTYPEID", "FNumber", BomBillTypeNumber);
            string orgId = JmMaterialHelper.QueryBaseDataId(ctx, "T_ORG_ORGANIZATIONS", "FORGID", "FNumber", OrgNumber);

            if (!string.IsNullOrEmpty(billTypeId))
            {
                view.Model.SetItemValueByID("FBILLTYPE", billTypeId, 0);
            }
            if (!string.IsNullOrEmpty(orgId))
            {
                view.Model.SetItemValueByID("FCreateOrgId", orgId, 0);
                view.Model.SetItemValueByID("FUseOrgId", orgId, 0);
            }

            // 父项物料（梯号）
            view.Model.SetItemValueByID("FMATERIALID", parentMaterialId.ToString(), 0);

            // 父项单位 = 父项物料基本计量单位
            long parentUnit = QueryMaterialBaseUnit(ctx, parentMaterialId);
            if (parentUnit > 0)
            {
                view.Model.SetItemValueByID("FUNITID", parentUnit.ToString(), 0);
            }

            view.Model.SetValue("FBOMCATEGORY", BomCategory);
            view.Model.SetValue("FBOMUSE", BomUse);
            // FNumber（BOM 编码）未设置，依赖系统自动编号；演示环境编码规则待确认后按需补设

            // ---- 子项（FTreeEntity）----
            // FireOnLoad 后新增单据预置 1 个空子项行，必须清空，否则 CreateNewEntryRow 后行号从 1 开始
            DynamicObjectCollection entryCol = view.Model.DataObject[BomEntryKey] as DynamicObjectCollection;
            if (entryCol != null)
            {
                entryCol.Clear();
            }

            for (int i = 0; i < childMaterialIds.Count; i++)
            {
                long childId = childMaterialIds[i];
                if (childId <= 0) continue;

                view.Model.CreateNewEntryRow(BomEntryKey);
                int row = view.Model.GetEntryRowCount(BomEntryKey) - 1;

                // 子项物料（基础资料引用字段，SetItemValueByID）
                view.Model.SetItemValueByID("FMATERIALIDCHILD", childId.ToString(), row);

                // 子项单位 = 子项物料基本计量单位
                long childUnit = QueryMaterialBaseUnit(ctx, childId);
                if (childUnit > 0)
                {
                    view.Model.SetItemValueByID("FCHILDUNITID", childUnit.ToString(), row);
                }

                view.Model.SetValue("FMATERIALTYPE", "标准件", row);
                view.Model.SetValue("FDOSAGETYPE", "变动", row);
                view.Model.SetValue("FNUMERATOR", 1m, row);
                view.Model.SetValue("FDENOMINATOR", 1m, row);
                view.Model.SetValue("FEFFECTDATE", DateTime.Now, row);
                view.Model.SetValue("FISSUETYPE", "直接领料", row);
            }

            // 保存 + 提交 + 审核（用户确认 2026-08-05）
            JmMaterialHelper.SaveSubmitAudit(ctx, view, BomFormId);
        }

        /// <summary>
        /// 查物料的基本计量单位内码。
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
    }
}