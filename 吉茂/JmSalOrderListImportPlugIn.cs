using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Util;
using System;
using System.ComponentModel;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单列表导入按钮插件
    /// 功能：销售订单列表点击【导入采购合同PDF】按钮，弹动态表单（上传 PDF 弹窗），
    /// 由 JmSalOrderImportPlugIn 处理上传与生成。关闭弹窗后刷新列表。
    ///
    /// 实现参考：鹤见泵业 SalOrderListImportPLugIn.cs（列表按钮 → DynamicFormShowParameter → ShowForm）。
    /// </summary>
    [Description("吉茂-销售订单列表导入"), HotUpdate]
    public class JmSalOrderListImportPlugIn : AbstractListPlugIn
    {
        /// <summary>列表按钮标识（在 BOS 元数据销售订单列表按钮注册）</summary>
        public const string BarItemImportPdf = "tbImportKonePdf";

        /// <summary>上传弹窗动态表单唯一标识（演示环境在 BOS 设计器配置后填入 FormId）</summary>
        public const string ImportPdfFormId = "Jm_KONE_PdfImportForm";

        /// <summary>
        /// 列表按钮点击事件：打开上传 PDF 的动态表单。
        /// </summary>
        /// <param name="e">按钮事件参数</param>
        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);
            if (e.BarItemKey != BarItemImportPdf) return;

            DynamicFormShowParameter forParameter = new DynamicFormShowParameter();
            forParameter.FormId = ImportPdfFormId;
            this.View.ShowForm(forParameter, new Action<FormResult>(delegate(FormResult results)
            {
                this.View.Refresh();
            }));
        }
    }
}
