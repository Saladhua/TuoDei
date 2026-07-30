using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-客户物料对应表-按钮插件
    /// 功能：点击【根据图号获取物料】按钮，遍历明细行的图号(F_CustLI_DrawingNo)，
    /// 批量从物料基础资料(T_BD_MATERIAL)匹配物料编码，将匹配到的物料ID回填到物料字段(FMATERIALID)。
    /// 采用"先收集所有图号→批量IN查询→逐行回填"的二段式设计，避免循环内逐行查DB。
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-客户物料对应表-根据图号获取物料")]
    public class LinLanXQCustMatMappingGetMaterialPlugIn : AbstractBillPlugIn
    {
        /// <summary>
        /// 明细工具栏按钮点击事件处理：触发图号→物料批量匹配逻辑
        /// </summary>
        /// <param name="e">按钮事件参数，包含按钮标识BarItemKey</param>
        public override void EntryBarItemClick(BarItemClickEventArgs e)
        {
            base.EntryBarItemClick(e);

            // 只处理【根据图号获取物料】按钮，按钮标识在BOS元数据中注册
            if (!e.BarItemKey.Equals("F_CustLI_GetMatByDrawing", StringComparison.OrdinalIgnoreCase)) return;

            DynamicObject billObj = this.View.Model.DataObject;
            if (billObj == null) return;

            // 获取明细行集合：Sal_CustMatMappingEntry 为客户物料对应表的单据体标识
            DynamicObjectCollection entryCollection = billObj["Sal_CustMatMappingEntry"] as DynamicObjectCollection;
            if (entryCollection == null || entryCollection.Count == 0) return;

            // ---- 第一遍遍历：收集所有图号 ----
            // 同时建立行号→图号的映射，用于后续按行回填时快速查找
            List<string> drawingNoList = new List<string>();
            Dictionary<int, string> rowDrawingMap = new Dictionary<int, string>();

            int rowIndex = 0;
            foreach (DynamicObject entry in entryCollection)
            {
                string drawingNo = "";
                if (entry["F_CustLI_DrawingNo"] != null)
                {
                    drawingNo = entry["F_CustLI_DrawingNo"].ToString().Trim();
                }

                // 忽略图号为空的空行
                if (!string.IsNullOrEmpty(drawingNo))
                {
                    drawingNoList.Add(drawingNo);
                    rowDrawingMap[rowIndex] = drawingNo;
                }
                rowIndex++;
            }

            if (drawingNoList.Count == 0) return;

            // ---- 批量查询：一次查出所有图号的匹配物料 ----
            // 用字典缓存查询结果，key=图号, value=物料内码
            Dictionary<string, long> drawingToMaterialMap = LinLanXQMaterialHelper.BatchQueryMaterialByDrawingNos(this.View.Context, drawingNoList);

            // ---- 第二遍遍历：逐行回填匹配结果 ----
            rowIndex = 0;
            int matchCount = 0;
            int failCount = 0;
            foreach (DynamicObject entry in entryCollection)
            {
                if (rowDrawingMap.ContainsKey(rowIndex))
                {
                    string drawingNo = rowDrawingMap[rowIndex];
                    if (drawingToMaterialMap.ContainsKey(drawingNo))
                    {
                        long materialId = drawingToMaterialMap[drawingNo];
                        this.View.Model.SetValue("FMATERIALID", materialId, rowIndex);
                        matchCount++;
                    }
                    else
                    {
                        // 图号在物料基础资料中不存在，计入失败计数
                        failCount++;
                    }
                }
                rowIndex++;
            }

            // 弹窗提示最终匹配结果，方便用户了解哪些行匹配失败需要手动处理
            if (failCount > 0)
            {
                this.View.ShowMessage("匹配完成：" + matchCount.ToString() + "条成功，" + failCount.ToString() + "条未匹配到物料");
            }
            else
            {
                this.View.ShowMessage("匹配完成：共" + matchCount.ToString() + "条，全部匹配成功");
            }
        }


    }
}
