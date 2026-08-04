using Kingdee.BOS.JSON;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.DynamicForm.PlugIn.ControlModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单导入弹窗表单插件
    ///
    /// 功能（销售订单界面加"导入"按钮 → 弹页面 → 上传 PDF → 解析 → 生成销售订单）：
    ///   1. CustomEvents：HtmlFile 上传控件上传完成后，收集全部 PDF 的服务器物理路径
    ///   2. ButtonClick：逐份解析 PDF（JmPdfParseHelper）→ 先查料再动作（JmMaterialHelper）→
    ///      数据包保存销售订单（JmSalOrderSaveHelper），汇总每份结果（成功/跳过/失败）
    ///
    /// 多份 PDF 批量：上传控件允许多选，逐份处理，单份失败不阻断其余。
    ///
    /// 演示环境配置（BOS 设计器）：
    ///   - 新建动态表单（唯一标识 k294935dab5cb40c6b47bfa0b5108d7ef），添加 HtmlFile 上传控件（Key = F_OKVA_FileUpdate_83g，允许多文件、扩展名 pdf）
    ///   - 添加"导入"按钮（Key = F_OKVA_Button_re5）
    ///   - 添加"取消"按钮（Key = F_OKVA_Button_apv）
    ///   - 动态表单插件注册本类
    ///
    /// 实现参考：鹤见泵业 SalOrderImportPlugIn.cs（上传回调 + 按钮处理）。
    /// </summary>
    [Description("吉茂-销售订单导入弹窗"), HotUpdate]
    public class JmSalOrderImportPlugIn : AbstractDynamicFormPlugIn
    {
        /// <summary>上传文件服务目录（上传控件默认存储路径）</summary>
        private const string FileUploadServicesDir = "FileUploadServices/UploadFiles";

        /// <summary>导入（确定）按钮 Key（与 BOS 元数据动态表单按钮一致）</summary>
        private const string btnOkKey = "F_OKVA_Button_re5";

        /// <summary>取消按钮 Key（与 BOS 元数据动态表单按钮一致，点击关闭弹窗不导入）</summary>
        private const string btnCancelKey = "F_OKVA_Button_apv";

        /// <summary>上传控件 Key（与 BOS 元数据动态表单 HtmlFile 控件一致）</summary>
        private const string attachUploadKey = "F_OKVA_FileUpdate_83g";

        /// <summary>已上传 PDF 的服务端物理路径集合</summary>
        private readonly List<string> _filePaths = new List<string>();

        /// <summary>
        /// 上传控件自定义事件：文件上传完成后收集服务器物理路径。
        /// </summary>
        /// <param name="e">自定义事件参数（FileChanged）</param>
        public override void CustomEvents(CustomEventsArgs e)
        {
            base.CustomEvents(e);
            if (!e.Key.EqualsIgnoreCase(attachUploadKey)) return;

            // 设置回调参数，确保上传完成触发 FileChanged
            this.View.GetControl(attachUploadKey).SetCustomPropertyValue("NeedCallback", true);
            this.View.GetControl(attachUploadKey).SetCustomPropertyValue("IsRequesting", false);

            if (!e.EventName.EqualsIgnoreCase("FileChanged")) return;

            JSONObject jSONObject = KDObjectConverter.DeserializeObject<JSONObject>(e.EventArgs);
            if (jSONObject == null) return;

            JSONArray jSONArray = new JSONArray(jSONObject["NewValue"].ToString());
            _filePaths.Clear();
            foreach (object item in jSONArray)
            {
                Dictionary<string, object> dict = item as Dictionary<string, object>;
                if (dict == null) continue;
                if (!dict.ContainsKey("ServerFileName")) continue;

                string serverFileName = dict["ServerFileName"].ToString();
                if (CheckFile(serverFileName))
                {
                    _filePaths.Add(GetFilePath(serverFileName));
                }
            }

            if (_filePaths.Count > 0)
            {
                this.EnableButton(btnOkKey, true);
                this.View.ShowMessage(string.Format("已上传 {0} 份 PDF，可点击导入", _filePaths.Count), MessageBoxType.Advise);
            }
            else
            {
                this.EnableButton(btnOkKey, false);
            }
        }

        /// <summary>
        /// 导入按钮点击事件：逐份解析并生成销售订单。
        /// </summary>
        /// <param name="e">按钮事件参数</param>
        public override void ButtonClick(ButtonClickEventArgs e)
        {
            base.ButtonClick(e);

            // 取消按钮：关闭弹窗，不执行导入
            if (e.Key.EqualsIgnoreCase(btnCancelKey))
            {
                this.View.Close();
                return;
            }

            if (!e.Key.EqualsIgnoreCase(btnOkKey)) return;

            if (_filePaths.Count == 0)
            {
                this.View.ShowWarnningMessage("请先上传 PDF 文件！");
                return;
            }

            List<string> logLines = new List<string>();
            int successCount = 0;
            bool allSuccess = true;
            foreach (string filePath in _filePaths)
            {
                string fileName = Path.GetFileName(filePath);
                try
                {
                    byte[] pdfBytes = File.ReadAllBytes(filePath);
                    JmPdfOrder order = JmPdfParseHelper.Parse(pdfBytes);
                    if (!order.Success)
                    {
                        logLines.Add(fileName + "：解析失败：" + order.ErrorMessage);
                        continue;
                    }

                    // 收集梯号（成品，缺失自动建）与组件图号（客户负责，缺失报错）
                    List<string> tierNumbers = new List<string>();
                    List<string> componentNumbers = new List<string>();
                    foreach (JmPdfTier tier in order.Tiers)
                    {
                        if (!string.IsNullOrEmpty(tier.TierNo) && !tierNumbers.Contains(tier.TierNo))
                        {
                            tierNumbers.Add(tier.TierNo);
                        }
                        if (tier.Axis != null && !componentNumbers.Contains(tier.Axis.Material))
                        {
                            componentNumbers.Add(tier.Axis.Material);
                        }
                        if (tier.UpModule != null && !componentNumbers.Contains(tier.UpModule.Material))
                        {
                            componentNumbers.Add(tier.UpModule.Material);
                        }
                        if (tier.DownModule != null && !componentNumbers.Contains(tier.DownModule.Material))
                        {
                            componentNumbers.Add(tier.DownModule.Material);
                        }
                    }

                    JmSalOrderSaveResult saveResult = JmSalOrderSaveHelper.SaveSaleOrder(
                        this.View.Context, order, tierNumbers, componentNumbers);

                    if (saveResult.Success)
                    {
                        successCount++;
                        logLines.Add(fileName + "：成功，销售订单 " + saveResult.BillNo);
                    }
                    else
                    {
                        logLines.Add(fileName + "：" + saveResult.Message);
                        allSuccess = false;
                        this.View.ShowMessage(fileName + "：" + saveResult.Message, MessageBoxType.Error);
                    }
                }
                catch (Exception ex)
                {
                    logLines.Add(fileName + "：异常：" + ex.Message);
                    allSuccess = false;
                    this.View.ShowMessage(fileName + "：异常：" + ex.Message, MessageBoxType.Error);
                }
            }

            // 全部成功：关闭弹窗（列表插件 ShowForm 回调触发列表刷新），新订单在列表中可见；
            // 存在失败/异常时弹窗保持打开便于重试
            if (allSuccess)
            {
                this.View.Close();
            }
        }

        /// <summary>
        /// 启用/禁用导入按钮（未上传完成前按钮置灰）。
        /// </summary>
        /// <param name="key">按钮 Key</param>
        /// <param name="bEnable">是否启用</param>
        private void EnableButton(string key, bool bEnable)
        {
            this.View.GetControl<Button>(key).Enabled = bEnable;
        }

        /// <summary>
        /// 校验上传文件名是否为 PDF。
        /// </summary>
        /// <param name="fileName">文件名/路径</param>
        /// <returns>是 PDF 返回 true</returns>
        private bool CheckFile(string fileName)
        {
            return !string.IsNullOrEmpty(fileName)
                && fileName.Trim().EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 上传控件返回的服务器文件名转物理路径。
        /// </summary>
        /// <param name="serverFileName">服务器文件名</param>
        /// <returns>物理路径</returns>
        private string GetFilePath(string serverFileName)
        {
            return PathUtils.GetPhysicalPath(FileUploadServicesDir, serverFileName);
        }
    }
}
