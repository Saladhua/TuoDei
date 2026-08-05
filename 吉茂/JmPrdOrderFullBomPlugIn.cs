using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App;
using Kingdee.BOS.Contracts;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-生产订单"完整BOM"页签表单插件
    ///
    /// 生产订单首页画一个页签（明细的子行子表），母项 = 生产订单明细行物料（梯号）。
    /// 页签上有按钮"BOM整体展示"，点击后按母项物料递归查询物料清单（BOM）数据并填充页签。
    ///
    /// 页签子表元数据（用户提供 2026-08-05）：
    ///   子表标识  F_OKVA_SubEntity_83g（CreateNewEntryRow 用实体键）
    ///   物理表名  OKVA_t_Cust_Entry100031，分录主键 FBOSEntryID
    ///   父分录    = 生产订单明细（母项 = 明细行物料）
    ///   按钮事件  ButtonClick
    ///
    /// 页签字段（序号=物料清单自身序号/层级，用户确认）：
    ///   序号、图号、名称、数量、单位、材质、材料规格、工序1-8、生产部门、备注。
    /// BOS 字段标识为拟定占位（F_CustLI_ 前缀，配置区），待用户提供实际标识后替换。
    ///
    /// 递归查 BOM：逐层批量查 T_ENG_BOM 子项（每层一次 SQL，字典缓存，避免循环内查 DB）。
    /// </summary>
    [Description("吉茂-生产订单完整BOM页签"), HotUpdate]
    public class JmPrdOrderFullBomPlugIn : AbstractBillPlugIn
    {
        // ==================== 配置区（BOS 标识拟定占位，待用户提供实际值后替换） ====================

        /// <summary>页签子表标识（CreateNewEntryRow 实体键 / DataObject 取集合）</summary>
        private const string BomSubEntityKey = "F_OKVA_SubEntity_83g";

        /// <summary>按钮标识（"BOM整体展示"按钮，事件 ButtonClick）</summary>
        private const string BomButtonKey = "ButtonClick";

        /// <summary>页签字段标识（与物理表 OKVA_t_Cust_Entry100031 对应）</summary>
        private const string FldSeq = "F_CustLI_BomSeq";        // 序号（=物料清单自身序号）
        private const string FldCode = "F_CustLI_BomCode";      // 图号
        private const string FldName = "F_CustLI_BomName";      // 名称
        private const string FldQty = "F_CustLI_BomQty";        // 数量
        private const string FldUnit = "F_CustLI_BomUnit";      // 单位
        private const string FldMaterial = "F_CustLI_Material"; // 材质
        private const string FldSpec = "F_CustLI_BomSpec";      // 材料规格
        private const string FldDept = "F_CustLI_BomDept";      // 生产部门
        private const string FldNote = "F_CustLI_BomNote";      // 备注

        // 工序1-8 字段标识
        private static readonly string[] ProcessFieldKeys = new string[]
        {
            "F_CustLI_Process1", "F_CustLI_Process2", "F_CustLI_Process3", "F_CustLI_Process4",
            "F_CustLI_Process5", "F_CustLI_Process6", "F_CustLI_Process7", "F_CustLI_Process8"
        };

        /// <summary>
        /// 按钮点击："BOM整体展示"——取当前明细行物料，递归展开 BOM 填充页签子表。
        /// </summary>
        /// <param name="e">按钮事件参数</param>
        public override void ButtonClick(ButtonClickEventArgs e)
        {
            base.ButtonClick(e);
            if (e.Key != BomButtonKey) return;

            // 取当前明细行物料（梯号，母项）
            long rootMaterialId = GetCurrentRowMaterialId();
            if (rootMaterialId <= 0)
            {
                this.View.ShowMessage("请先选中生产订单明细行（当前行为空或缺物料）。");
                return;
            }

            // 递归展开 BOM，收集页签行（序号=物料清单自身序号）
            List<BomRow> rows = JmBomQueryHelper.ExpandBom(this.Context, rootMaterialId);
            if (rows.Count == 0)
            {
                this.View.ShowMessage("该物料未查询到有效物料清单（BOM）。");
                return;
            }

            FillSubEntity(rows);
        }

        /// <summary>
        /// 取当前生产订单明细行物料内码（母项）。
        /// 优先取当前选中明细行，取不到则取首个明细行。
        /// </summary>
        /// <returns>物料内码；无返回 0</returns>
        private long GetCurrentRowMaterialId()
        {
            try
            {
                object val = this.Model.GetValue("FMaterialId");
                DynamicObject mat = val as DynamicObject;
                if (mat != null)
                {
                    long id = Convert.ToInt64(mat["Id"]);
                    if (id > 0) return id;
                }
            }
            catch
            {
                // 当前行无值或字段取不到，忽略后走明细集合兜底
            }

            // 兜底：取明细第一个非空物料
            try
            {
                DynamicObjectCollection moEntry = this.Model.DataObject["MOEntry"] as DynamicObjectCollection;
                if (moEntry == null) return 0;
                foreach (DynamicObject entry in moEntry)
                {
                    DynamicObject mat = entry["FMaterialId"] as DynamicObject;
                    if (mat == null) continue;
                    long id = Convert.ToInt64(mat["Id"]);
                    if (id > 0) return id;
                }
            }
            catch
            {
                return 0;
            }
            return 0;
        }

        /// <summary>
        /// 将展开结果填充到页签子表。
        /// 先清空旧数据，再逐行 CreateNewEntryRow + SetValue（沿用双 Key 实证模式）。
        /// </summary>
        /// <param name="rows">展开的 BOM 行集合</param>
        private void FillSubEntity(List<BomRow> rows)
        {
            // 清空旧数据（子表集合直接 Clear）
            DynamicObjectCollection subEntity = this.Model.DataObject[BomSubEntityKey] as DynamicObjectCollection;
            if (subEntity != null)
            {
                subEntity.Clear();
            }

            foreach (BomRow row in rows)
            {
                this.Model.CreateNewEntryRow(BomSubEntityKey);
                int r = this.Model.GetEntryRowCount(BomSubEntityKey) - 1;

                this.Model.SetValue(FldSeq, row.Seq, r);
                this.Model.SetValue(FldCode, row.Code, r);
                this.Model.SetValue(FldName, row.Name, r);
                this.Model.SetValue(FldQty, row.Qty, r);
                this.Model.SetValue(FldUnit, row.Unit, r);
                this.Model.SetValue(FldMaterial, row.Material, r);
                this.Model.SetValue(FldSpec, row.Spec, r);

                for (int i = 0; i < ProcessFieldKeys.Length; i++)
                {
                    string p = i < row.Processes.Count ? row.Processes[i] : "";
                    this.Model.SetValue(ProcessFieldKeys[i], p, r);
                }

                this.Model.SetValue(FldDept, row.Dept, r);
                this.Model.SetValue(FldNote, row.Note, r);
            }
        }
    }

    /// <summary>
    /// 吉茂-BOM 展开查询帮助类（递归逐层查 T_ENG_BOM 子项，字典缓存避免循环内查 DB）。
    /// </summary>
    public static class JmBomQueryHelper
    {
        /// <summary>
        /// 从根物料递归展开 BOM 至最底层，返回全部页签行。
        /// 用队列逐层展开，每层一次性批量查子项（避免循环内查 DB）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="rootMaterialId">根物料（梯号）内码</param>
        /// <returns>展开行集合（BOM 树顺序）</returns>
        public static List<BomRow> ExpandBom(Context ctx, long rootMaterialId)
        {
            List<BomRow> result = new List<BomRow>();

            // 待展开物料队列
            Queue<long> parents = new Queue<long>();
            parents.Enqueue(rootMaterialId);

            // 防死循环：已展开过的物料内码
            HashSet<long> visited = new HashSet<long>();
            visited.Add(rootMaterialId);

            while (parents.Count > 0)
            {
                // 本层所有父物料
                List<long> layerParents = new List<long>(parents);
                parents.Clear();

                // 批量查本层所有父物料的 BOM 子项
                Dictionary<long, List<BomRow>> layerRows = QueryChildrenByParents(ctx, layerParents);

                foreach (long parent in layerParents)
                {
                    List<BomRow> childRows;
                    if (!layerRows.TryGetValue(parent, out childRows)) continue;

                    foreach (BomRow row in childRows)
                    {
                        result.Add(row);

                        // 子项若仍有下级 BOM，下一层展开（防死循环）
                        if (!visited.Contains(row.MaterialId))
                        {
                            visited.Add(row.MaterialId);
                            parents.Enqueue(row.MaterialId);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 批量查一批父物料的 BOM 子项（含子项物料基础信息）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="parentIds">父物料内码集合</param>
        /// <returns>按父物料分组（内码 → 子项行集合）</returns>
        private static Dictionary<long, List<BomRow>> QueryChildrenByParents(Context ctx, List<long> parentIds)
        {
            Dictionary<long, List<BomRow>> map = new Dictionary<long, List<BomRow>>();
            if (parentIds == null || parentIds.Count == 0) return map;

            for (int i = 0; i < parentIds.Count; i++)
            {
                map[parentIds[i]] = new List<BomRow>();
            }

            string ids = string.Join(",", parentIds);
            string sql = string.Format(
                @"SELECT b.FMATERIALID AS FMaterialId
                        ,ch.FSEQ AS FSEQ
                        ,ch.FMATERIALIDCHILD AS FChildMaterialId
                        ,ch.FQTY AS FQty
                        ,child.FNUMBER AS FNumber
                        ,childName.FNAME AS FName
                        ,child.FSPECIFICATION AS FSpecification
                        ,child.F_CustLI_Material AS F_CustLI_Material
                        ,unit.FNUMBER AS FUnitNumber
                        ,ch.F_CustLI_Process1 AS FP1
                        ,ch.F_CustLI_Process2 AS FP2
                        ,ch.F_CustLI_Process3 AS FP3
                        ,ch.F_CustLI_Process4 AS FP4
                        ,ch.F_CustLI_Process5 AS FP5
                        ,ch.F_CustLI_Process6 AS FP6
                        ,ch.F_CustLI_Process7 AS FP7
                        ,ch.F_CustLI_Process8 AS FP8
                        ,ch.F_CustLI_BomDept AS FDept
                        ,ch.F_CustLI_BomNote AS FNote
                  FROM T_ENG_BOM b
                  INNER JOIN T_ENG_BOMCHILD ch ON ch.FBOMID = b.FID
                  INNER JOIN T_BD_MATERIAL child ON child.FMATERIALID = ch.FMATERIALIDCHILD
                  LEFT JOIN T_BD_MATERIAL_L childName
                             ON childName.FMATERIALID = child.FMATERIALID
                            AND childName.FLOCALEID = {0}
                  LEFT JOIN T_BD_UNIT unit ON unit.FUNITID = ch.FCHILDUNITID
                  WHERE b.FMATERIALID IN ({1})
                    AND b.FDOCUMENTSTATUS IN ('A','C')
                  ORDER BY b.FMATERIALID, ch.FSEQ",
                ctx.UserLocale.LCID, ids);

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows == null || rows.Count == 0) return map;

            foreach (DynamicObject row in rows)
            {
                long parentId = Convert.ToInt64(row["FMaterialId"]);
                if (!map.ContainsKey(parentId)) continue;

                BomRow br = new BomRow
                {
                    MaterialId = Convert.ToInt64(row["FChildMaterialId"]),
                    Seq = ObjectToString(row["FSEQ"]),
                    Code = ObjectToString(row["FNumber"]),
                    Name = ObjectToString(row["FName"]),
                    Qty = ObjectToString(row["FQty"]),
                    Unit = ObjectToString(row["FUnitNumber"]),
                    Material = ObjectToString(row["F_CustLI_Material"]),
                    Spec = ObjectToString(row["FSpecification"]),
                    Dept = ObjectToString(row["FDept"]),
                    Note = ObjectToString(row["FNote"]),
                };
                br.Processes.Add(ObjectToString(row["FP1"]));
                br.Processes.Add(ObjectToString(row["FP2"]));
                br.Processes.Add(ObjectToString(row["FP3"]));
                br.Processes.Add(ObjectToString(row["FP4"]));
                br.Processes.Add(ObjectToString(row["FP5"]));
                br.Processes.Add(ObjectToString(row["FP6"]));
                br.Processes.Add(ObjectToString(row["FP7"]));
                br.Processes.Add(ObjectToString(row["FP8"]));

                map[parentId].Add(br);
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

    /// <summary>
    /// 吉茂-BOM 展开行模型（页签每行数据）。
    /// </summary>
    public class BomRow
    {
        /// <summary>子项物料内码（用于下一层展开）</summary>
        public long MaterialId { get; set; }

        /// <summary>序号（=物料清单自身序号）</summary>
        public string Seq { get; set; }

        /// <summary>图号</summary>
        public string Code { get; set; }

        /// <summary>名称</summary>
        public string Name { get; set; }

        /// <summary>数量</summary>
        public string Qty { get; set; }

        /// <summary>单位</summary>
        public string Unit { get; set; }

        /// <summary>材质</summary>
        public string Material { get; set; }

        /// <summary>材料规格</summary>
        public string Spec { get; set; }

        /// <summary>生产部门</summary>
        public string Dept { get; set; }

        /// <summary>备注</summary>
        public string Note { get; set; }

        /// <summary>工序1-8</summary>
        public List<string> Processes { get; set; }

        public BomRow()
        {
            Processes = new List<string>();
        }
    }
}