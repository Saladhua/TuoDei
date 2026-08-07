using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata.EntityElement;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
/// <summary>
    /// 吉茂-生产订单保存时自动填充"完整BOM"页签子表 + 写入工艺要求 操作插件
    ///
    /// 目的（用户确认 2026-08-07）：生产订单（PRD_MO）保存时，自动为每行明细（母项=梯号物料）
    ///   递归展开物料清单（BOM）并填充页签子表，替代用户逐个点击"BOM整体展示"按钮；
    ///   同时将来源销售订单明细工艺要求汇总写入生产订单表头备注 FDescription。
    ///
    /// 触发：PRD_MO 单据保存（Save）操作，事件 BeforeExecuteOperationTransaction
    ///   （保存事务前置，事务内，之后框架执行核心保存逻辑，可一并持久化）。
    /// 与现有按钮插件（JmPrdOrderFullBomPlugIn）并存保留，本插件负责保存时自动填充。
    ///
    /// 数据访问红线（AGENTS.md §8 事故复盘结论）：Save 场景 e.DataEntitys 可能 null，
    ///   必须使用 e.SelectedRows（元素为 ExtendedDataEntity，取其 DataEntity 为单据 DynamicObject）。
    ///
    /// 工艺要求补充（用户确认 2026-08-07）：明细行来源销售订单头内码字段 FSALEORDERID
    ///   （指向销售订单【表头】FID），工艺字段存在销售订单明细分录 T_SAL_ORDERENTRY，
    ///   由 JmSaleTechMemoHelper.GetSaleOrderTechMemo 按销售订单头内码批量反查全部分录工艺、
    ///   逐销售订单换行连接写入表头 Description。无销售来源（手工新增非下推）保留表头现状不写入。
    ///
    /// 页签子表元数据（用户提供，与 JmPrdOrderFullBomPlugIn 一致）：
    ///   子表实体键   F_OKVA_Entity_83g（ORM 调取值会空引用，仅作注释说明）
    ///   子表集合键   OKVA_Cust_Entry100031（取集合用）
    ///   物理表名    OKVA_t_Cust_Entry100031，分录主键 FBOSEntryID
    ///   父分录       = 生产订单明细 MOEntry 行（母项 = 明细行物料）
    ///
    /// 填充字段（序号=物料清单自身序号/层级，数量=BOM 子项分子，可选列受 EnableOptionalColumns 开关控制）：
    ///   序号/图号/名称/数量/单位/材料规格 + 工序1-8 +（可选）材质/生产部门/备注。
    /// 与 JmPrdOrderFullBomPlugIn.FillSubEntity 保持相同填充规则，保证两入口一致。
    /// </summary>
    [Description("吉茂-生产订单保存时自动填充完整BOM页签"), HotUpdate]
    public class JmPrdOrderFillBomSaveServicePlugIn : AbstractOperationServicePlugIn
    {
        // ==================== 配置区（BOS 标识拟定占位，与 JmPrdOrderFullBomPlugIn 保持一致） ====================

        /// <summary>
        /// 生产订单明细行集合键。取值取 ORM 实体标识 TreeEntity
        /// （注意：ORM 键不同于表单控件键 MOEntry，表单插件 Model.DataObject["MOEntry"] 用 MOEntry；
        ///   本插件在 BeforeExecuteOperationTransaction 取集合及 OnPreparePropertys 声明字段均用 ORM 标识 TreeEntity）
        /// </summary>
        private const string MoEntryCollectionKey = "TreeEntity";

        /// <summary>页签子表集合键（ORM 名，取集合用；实体键 F_OKVA_Entity_83g 取集合会空引用）</summary>
        private const string BomSubCollectionKey = "OKVA_Cust_Entry100031";

        /// <summary>页签子表实体键（BusinessInfo.GetEntryEntity 元数据取子实体用，与表单插件实体键一致）</summary>
        private const string BomSubEntityKey = "F_OKVA_SubEntity_83g";

        /// <summary>
        /// 生产订单表头备注字段（标准字段 FDescription，ORM 属性名去 F = Description，用户确认 2026-08-07）
        /// 工艺要求与下推转换插件 JmSalOrderToProdOrderConvertPlugIn 同一落点。
        /// </summary>
        private const string TargetDescriptionFieldKey = "Description";

        /// <summary>
        /// 生产订单明细行来源销售订单头内码字段标识（用户确认 2026-08-07：MOEntry 明细 FSALEORDERID = 销售订单头内码 FID）
        /// 取值取 ORM 实体字段标识 SALEORDERID（ORM 标识去 F，物理字段为 FSALEORDERID；
        ///   与 MoEntryCollectionKey 用 ORM 标识 TreeEntity 同理）
        /// 工艺字段存在销售订单明细分录，需按销售订单头内码批量反查。
        /// </summary>
        private const string FldSaleOrderId = "SALEORDERID";

        /// <summary>
        /// 明细行物料引用字段标识（母项）。取值取 ORM 实体字段标识 MaterialId
        /// （注意：ORM 字段标识不同于表单控件键 FMaterialId，与 MoEntryCollectionKey 用 ORM 标识 TreeEntity 同理）
        /// </summary>
        private const string FldMaterial = "MaterialId";

        /// <summary>页签字段标识（与物理表 OKVA_t_Cust_Entry100031 对应，自定义字段保留 F）</summary>
        private const string FldSeq = "F_CustLI_BomSeq";        // 序号（=物料清单自身序号）
        private const string FldCode = "F_CustLI_BomCode";      // 图号
        private const string FldName = "F_CustLI_BomName";      // 名称
        private const string FldQty = "F_CustLI_BomQty";        // 数量
        private const string FldUnit = "F_CustLI_BomUnit";      // 单位
        private const string FldSpec = "F_CustLI_BomSpec";      // 材料规格
        private const string FldMaterialOpt = "F_CustLI_Material"; // 材质（可选列，未注册）
        private const string FldDept = "F_CustLI_BomDept";      // 生产部门（可选列，未注册）
        private const string FldNote = "F_CustLI_BomNote";      // 备注（可选列，未注册）

        /// <summary>工序1-8 字段标识</summary>
        private static readonly string[] ProcessFieldKeys = new string[]
        {
            "F_CustLI_Process1", "F_CustLI_Process2", "F_CustLI_Process3", "F_CustLI_Process4",
            "F_CustLI_Process5", "F_CustLI_Process6", "F_CustLI_Process7", "F_CustLI_Process8"
        };

        /// <summary>
        /// 保存前声明所需字段：明细行物料、明细行集合、掩表子表集合，避免多余 JOIN 与无效列。
        /// </summary>
        /// <param name="e">属性声明事件参数</param>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add(FldMaterial);
            e.FieldKeys.Add(FldSaleOrderId);
            e.FieldKeys.Add(MoEntryCollectionKey);
            e.FieldKeys.Add(BomSubCollectionKey);
            e.FieldKeys.Add(TargetDescriptionFieldKey);
        }

        /// <summary>
        /// 保存事务前置：遍历生产订单明细行，逐行自动填充"完整BOM"页签子表。
        /// 每行母料递归展开 BOM（JmBomQueryHelper.ExpandBom），展开结果非空则
        ///   清空该行页签子表旧数据后按行填充；无 BOM 的物料行保留现状，不清空已有子表。
        /// </summary>
        /// <param name="e">保存事务前置事件参数</param>
        public override void BeforeExecuteOperationTransaction(BeforeExecuteOperationTransaction e)
        {
            base.BeforeExecuteOperationTransaction(e);

            // 红线：Save 场景 e.DataEntitys 可能 null，必须用 e.SelectedRows 取 ExtendedDataEntity
            if (e.SelectedRows == null) return;

            foreach (ExtendedDataEntity data in e.SelectedRows)
            {
                DynamicObject billObj = data.DataEntity;
                if (billObj == null) continue;

                DynamicObjectCollection entryCol =
                    billObj[MoEntryCollectionKey] as DynamicObjectCollection;
                if (entryCol == null) continue;

                // 收集该单全部来源销售订单头内码（FSALEORDERID，用户确认 = 销售订单头内码 FID），用于工艺要求反查
                HashSet<long> saleOrderFids = new HashSet<long>();

                foreach (DynamicObject entry in entryCol)
                {
                    if (entry == null) continue;

                    long saleOrderId = GetReferenceId(entry[FldSaleOrderId]);
                    if (saleOrderId > 0 && saleOrderFids.Contains(saleOrderId) == false)
                    {
                        saleOrderFids.Add(saleOrderId);
                    }

                    // 取母项物料（梯号）；无物料跳过该行
                    long rootMaterialId = GetMaterialId(entry[FldMaterial]);
                    if (rootMaterialId <= 0) continue;

                    // 递归展开 BOM；无有效 BOM 保留该行现状，不填充
                    List<BomRow> rows = JmBomQueryHelper.ExpandBom(this.Context, rootMaterialId);
                    if (rows.Count == 0) continue;

                    // 取该明细行下页签子表集合并清空重填
                    FillSubEntity(entry, rows);
                }

                // 工艺要求汇总写入表头备注（与下推转换插件同一落点 FDescription，ORM 去 F=Description）
                // 无销售来源（手工新增非下推）保留表头现状，不写入
                FillSaleDescription(billObj, saleOrderFids);
            }
        }

        /// <summary>
        /// 工艺要求汇总写入生产订单表头备注字段：按收集到的销售订单头内码批量反查
        /// 销售订单明细分录工艺字段（JmSaleTechMemoHelper.GetSaleOrderTechMemo），
        /// 逐销售订单换行连接为一个整体文本，写入 billObj[Description]。
        /// 无销售来源（saleOrderFids 为空）保留表头现状，不写入。
        /// </summary>
        /// <param name="billObj">生产订单单据对象（表头根实体）</param>
        /// <param name="saleOrderFids">来源销售订单头内码集合</param>
        private void FillSaleDescription(DynamicObject billObj, HashSet<long> saleOrderFids)
        {
            if (saleOrderFids == null || saleOrderFids.Count == 0) return;

            // SQL 批量反查销售订单明细分录工艺字段，按销售订单头内码返回该单汇总文本
            Dictionary<long, string> dctFidToMemo = JmSaleTechMemoHelper.GetSaleOrderTechMemo(this.Context, saleOrderFids);
            if (dctFidToMemo.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            foreach (long fid in saleOrderFids)
            {
                string memo;
                if (dctFidToMemo.TryGetValue(fid, out memo))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(memo);
                }
            }
            if (sb.Length == 0) return;

            // 生产订单表头根实体，直接写备注字段（标准字段 ORM 去 F = Description）
            billObj[TargetDescriptionFieldKey] = sb.ToString();
        }

        /// <summary>
        /// 取基础资料引用字段的内码（引用对象取 DynamicObject["Id"]，否则转 long）。
        /// </summary>
        /// <param name="refObj">引用字段值</param>
        /// <returns>内码；无返回 0</returns>
        private long GetReferenceId(object refObj)
        {
            if (refObj == null) return 0;
            DynamicObject refData = refObj as DynamicObject;
            if (refData != null) return Convert.ToInt64(refData["Id"]);
            return Convert.ToInt64(refObj);
        }

        /// <summary>
        /// 取物料引用字段的内码（FMaterialId 为基础资料引用，取 DynamicObject["Id"]）。
        /// </summary>
        /// <param name="matObj">物料字段值</param>
        /// <returns>物料内码；无返回 0</returns>
        private long GetMaterialId(object matObj)
        {
            if (matObj == null) return 0;
            DynamicObject mat = matObj as DynamicObject;
            if (mat != null) return Convert.ToInt64(mat["Id"]);
            return Convert.ToInt64(matObj);
        }

        /// <summary>
        /// 将展开结果填充到该明细行的页签子表（生产订单 MOEntry 明细行下的子表）。
        /// 先清空该行子表全部旧行，再按 BomRow 逐行构造 DynamicObject 并 Add。
        /// 与表单插件填充规则一致：可选列（材质/生产部门/备注）受 EnableOptionalColumns 开关控制。
        /// </summary>
        /// <param name="entry">生产订单明细行对象（页签子表父行）</param>
        /// <param name="rows">展开的 BOM 行集合</param>
        /// <summary>负数临时主键计数器：服务端新增子实体行需负 EntryId 供 ORM 识别为"待新增行"并由框架生成正式主键</summary>
        private long _tempSubRowId = 0;

        private void FillSubEntity(DynamicObject entry, List<BomRow> rows)
        {
            DynamicObjectCollection sub = null;
            SubEntryEntity subEntry = this.BusinessInfo.GetEntryEntity(BomSubEntityKey) as SubEntryEntity;
            if (subEntry != null)
            {
                sub = subEntry.DynamicProperty.GetValue(entry) as DynamicObjectCollection;
            }
            if (sub == null)
            {
                sub = entry[BomSubCollectionKey] as DynamicObjectCollection;
            }
            if (sub == null) return;

            // 清空旧数据，再重新填充（与表单插件 DeleteEntryRow 全清语义一致）
            sub.Clear();

            // 建行用子单据体类型的 CreateInstance()（林蓝 LinLanXQMatPackSyncServicePlugIn 实证，
            // 非裸 new DynamicObject：CreateInstance 创建行携带动态实体元数据，ORM 能正确持久化。
            // 每行赋一个递减负数临时主键（FBOSEntryID 物理列，ORM 属性名 Id），
            // 框架保存时识别为"新增子目标实体行"并转换为正式主键，避免主键全 0 撞 pk_OKVA_t_Cust_Entry100031）
            foreach (BomRow row in rows)
            {
                DynamicObject newRow = (DynamicObject)sub.DynamicCollectionItemPropertyType.CreateInstance();
                newRow["Id"] = _tempSubRowId--;

                newRow[FldSeq] = row.Seq;
                newRow[FldCode] = row.Code;
                newRow[FldName] = row.Name;
                newRow[FldQty] = row.Qty;
                newRow[FldUnit] = row.Unit;
                newRow[FldSpec] = row.Spec;

                if (JmBomQueryHelper.EnableOptionalColumns)
                {
                    // 可选列已注册（EnableOptionalColumns=true）才填充，避免 BOS 无该字段抛异常
                    newRow[FldMaterialOpt] = row.Material;
                    newRow[FldDept] = row.Dept;
                    newRow[FldNote] = row.Note;
                }

                for (int i = 0; i < ProcessFieldKeys.Length; i++)
                {
                    string p = i < row.Processes.Count ? row.Processes[i] : "";
                    newRow[ProcessFieldKeys[i]] = p;
                }

                sub.Add(newRow);
            }
        }
    }
}
