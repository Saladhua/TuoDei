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
    /// 吉茂-物料查料/建料帮助类
    ///
    /// 先查再动作（用户确认 2026-08-03）：
    ///   1. 梯号（成品）：查不到则数据包保存创建（自制件 FErpClsID=2），再返回物料内码
    ///   2. 主轴/上模组/下模组（客户负责）：只查询引用，任一缺失报错中止该份 PDF
    ///
    /// 数据包保存方式：CreateNewBillView + BusinessDataServiceHelper.Save/Submit/Audit
    /// （实证参考：林蓝汽车 LinLanXQMatPackSyncServicePlugIn.cs）
    /// </summary>
    public static class JmMaterialHelper
    {
        // ==================== 配置区（演示环境需确认） ====================

        /// <summary>物料创建-物料分组编码（固定值，演示环境确认 TCOMM）</summary>
        public const string MaterialGroupNumber = "TCOMM";

        /// <summary>物料创建-基本计量单位编码（固定值，演示环境确认 tai）</summary>
        public const string BaseUnitNumber = "tai";

        /// <summary>物料创建-自制件标识（FErpClsID=2）</summary>
        public const string ErpClassSelfMade = "2";

        /// <summary>物料创建-启用状态（FForbidStatus=A）</summary>
        public const string ForbidStatusActive = "A";

        /// <summary>
        /// 确保物料就绪（先查再动作）：
        ///   批量查询全部料号（梯号+组件）→ 梯号缺失逐个建料 → 组件缺失收集报错。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="tierNumbers">梯号集合（成品，缺失自动创建）</param>
        /// <param name="componentNumbers">组件图号集合（主轴/上模组/下模组，客户负责，缺失报错）</param>
        /// <returns>料号 -> 物料内码（含新建的梯号）</returns>
        public static Dictionary<string, long> EnsureMaterials(Context ctx, List<string> tierNumbers, List<string> componentNumbers)
        {
            Dictionary<string, long> materialMap = new Dictionary<string, long>();

            // 一次性批量查询全部料号（避免循环内查 DB）
            List<string> allNumbers = new List<string>();
            if (tierNumbers != null) allNumbers.AddRange(tierNumbers);
            if (componentNumbers != null) allNumbers.AddRange(componentNumbers);
            if (allNumbers.Count > 0)
            {
                Dictionary<string, long> existMap = BatchQueryMaterialIds(ctx, allNumbers);
                foreach (KeyValuePair<string, long> kvp in existMap)
                {
                    materialMap[kvp.Key] = kvp.Value;
                }
            }

            // 梯号（成品）缺失 → 数据包创建（自制件）
            if (tierNumbers != null)
            {
                foreach (string tierNo in tierNumbers)
                {
                    if (string.IsNullOrEmpty(tierNo)) continue;
                    if (!materialMap.ContainsKey(tierNo))
                    {
                        long newId = CreateTierMaterial(ctx, tierNo);
                        materialMap[tierNo] = newId;
                    }
                }
            }

            // 组件（客户负责）缺失 → 收集缺失，报错中止该份 PDF
            List<string> missingComponents = new List<string>();
            if (componentNumbers != null)
            {
                foreach (string componentNo in componentNumbers)
                {
                    if (string.IsNullOrEmpty(componentNo)) continue;
                    if (!materialMap.ContainsKey(componentNo))
                    {
                        missingComponents.Add(componentNo);
                    }
                }
            }
            if (missingComponents.Count > 0)
            {
                throw new Exception(string.Format(
                    "客户组件物料在系统中未找到：{0}（请先由客户在系统中维护后重试）",
                    string.Join("、", missingComponents)));
            }

            return materialMap;
        }

        /// <summary>
        /// 批量按料号查询物料内码（一次 IN 查询）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="numbers">料号集合</param>
        /// <returns>料号 -> 物料内码（FMATERIALID）</returns>
        public static Dictionary<string, long> BatchQueryMaterialIds(Context ctx, List<string> numbers)
        {
            Dictionary<string, long> result = new Dictionary<string, long>();
            if (numbers == null || numbers.Count == 0) return result;

            List<string> distinct = numbers.Distinct().ToList();
            string inSql = string.Join(",", distinct.ConvertAll(n => "'" + n.Replace("'", "''") + "'"));

            string sql = string.Format(
                @"SELECT a1.FNUMBER AS FNUMBER, a1.FMATERIALID AS FMATERIALID
                  FROM T_BD_MATERIAL a1
                  WHERE a1.FNUMBER IN ({0})",
                inSql);

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows != null)
            {
                foreach (DynamicObject row in rows)
                {
                    string number = ObjectToString(row["FNUMBER"]);
                    long materialId = Convert.ToInt64(row["FMATERIALID"]);
                    if (!string.IsNullOrEmpty(number) && materialId > 0)
                    {
                        result[number] = materialId;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 按料号查询单个物料内码。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="number">料号</param>
        /// <returns>物料内码；不存在返回 0</returns>
        public static long QueryMaterialIdByNumber(Context ctx, string number)
        {
            if (string.IsNullOrEmpty(number)) return 0;
            Dictionary<string, long> map = BatchQueryMaterialIds(ctx, new List<string> { number });
            long id;
            if (map.TryGetValue(number, out id)) return id;
            return 0;
        }

        /// <summary>
        /// 创建梯号成品物料（数据包保存，自制件 FErpClsID=2），创建后提交并审核。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="number">梯号（物料编码），如 36151677</param>
        /// <returns>新建物料内码</returns>
        public static long CreateTierMaterial(Context ctx, string number)
        {
            if (string.IsNullOrEmpty(number))
            {
                throw new Exception("梯号为空，无法创建物料");
            }

            IBillView view = CreateNewBillView(ctx, "BD_MATERIAL", null);

            DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
            proxy.FireOnLoad();

            // 编码/名称（名称规则先与编码一致，演示环境可调整）
            view.Model.DataObject["Number"] = number;
            view.Model.DataObject["Name"] = number;

            // 物料分组（查内码 + SetItemValueByID，基础资料引用字段红线）
            string groupId = QueryBaseDataId(ctx, "T_BD_MATERIALGROUP", "FID", "FNumber", MaterialGroupNumber);
            if (!string.IsNullOrEmpty(groupId))
            {
                view.Model.SetItemValueByID("FMaterialGroup", groupId, 0);
                view.InvokeFieldUpdateService("FMaterialGroup", 0);
            }

            // 基本计量单位（查内码 + SetItemValueByID，基础资料引用字段红线）
            string unitId = QueryBaseDataId(ctx, "T_BD_UNIT", "FUNITID", "FNumber", BaseUnitNumber);
            if (!string.IsNullOrEmpty(unitId))
            {
                view.Model.SetItemValueByID("FBaseUnitId", unitId, 0);
                view.InvokeFieldUpdateService("FBaseUnitId", 0);
            }

            // 物料属性：自制件；启用状态
            view.Model.SetValue("FErpClsID", ErpClassSelfMade);
            view.Model.SetValue("FForbidStatus", ForbidStatusActive);

            SaveSubmitAudit(ctx, view, "BD_MATERIAL");

            long materialId = Convert.ToInt64(view.Model.DataObject["Id"]);
            return materialId;
        }

        /// <summary>
        /// 按编号查询基础资料内码（通用，兼容 long/GUID 主键，返回字符串）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="tableName">基础资料主表名</param>
        /// <param name="idField">主键字段名</param>
        /// <param name="numberField">编码字段名（销售员用 FStaffNumber，其余用 FNumber）</param>
        /// <param name="number">编号</param>
        /// <returns>基础资料内码字符串；未找到返回空串</returns>
        public static string QueryBaseDataId(Context ctx, string tableName, string idField, string numberField, string number)
        {
            if (string.IsNullOrEmpty(number)) return "";
            string sql = string.Format(
                @"SELECT a1.{0} AS FID
                  FROM {1} a1
                  WHERE a1.{2} = '{3}'",
                idField, tableName, numberField, number.Replace("'", "''"));

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows != null && rows.Count > 0 && rows[0]["FID"] != null)
            {
                return rows[0]["FID"].ToString();
            }
            return "";
        }

        /// <summary>
        /// 按名称查询基础资料内码（客户用，PDF Buyer 名称匹配 FNAME）。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="tableName">基础资料主表名</param>
        /// <param name="idField">主键字段名</param>
        /// <param name="name">名称</param>
        /// <returns>基础资料内码；未找到返回 0</returns>
        public static long QueryBaseDataByName(Context ctx, string tableName, string idField, string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string sql = string.Format(
                @"SELECT a1.{0} AS FID
                  FROM {1} a1
                  WHERE a1.FNAME = '{2}'",
                idField, tableName, name.Replace("'", "''"));

            var dbService = ServiceFactory.GetDBService(ctx);
            DynamicObjectCollection rows = dbService.ExecuteDynamicObject(ctx, sql);
            if (rows != null && rows.Count > 0 && rows[0]["FID"] != null)
            {
                return Convert.ToInt64(rows[0]["FID"]);
            }
            return 0;
        }

        /// <summary>
        /// 数据包保存：CreateNewBillView 构造单据 → Save → Submit → Audit。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="view">单据视图</param>
        /// <param name="formId">单据标识（错误提示用）</param>
        public static void SaveSubmitAudit(Context ctx, IBillView view, string formId)
        {
            IOperationResult saveResult = BusinessDataServiceHelper.Save(
                ctx, view.BillBusinessInfo, view.Model.DataObject,
                OperateOption.Create(), "Save");

            if (!saveResult.IsSuccess)
            {
                var errMsgs = saveResult.ValidationErrors.Select(x => x.Message);
                throw new Exception(string.Format("{0} 保存失败：{1}", formId, string.Join(",", errMsgs)));
            }

            long savedId = Convert.ToInt64(view.Model.DataObject["Id"]);
            object[] ids = new object[] { savedId };

            IOperationResult submitResult = BusinessDataServiceHelper.Submit(
                ctx, view.BillBusinessInfo, ids, "Submit", OperateOption.Create());
            if (!submitResult.IsSuccess)
            {
                var errMsgs = submitResult.ValidationErrors.Select(x => x.Message);
                throw new Exception(string.Format("{0} 提交失败：{1}", formId, string.Join(",", errMsgs)));
            }

            IOperationResult auditResult = BusinessDataServiceHelper.Audit(
                ctx, view.BillBusinessInfo, ids, OperateOption.Create());
            if (!auditResult.IsSuccess)
            {
                var errMsgs = auditResult.ValidationErrors.Select(x => x.Message);
                throw new Exception(string.Format("{0} 审核失败：{1}", formId, string.Join(",", errMsgs)));
            }
        }

        /// <summary>
        /// 创建单据视图（ADDNEW）。参考林蓝 LinLanXQMatPackSyncServicePlugIn.CreateNewBillView。
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="formId">单据标识</param>
        /// <param name="pkId">主键内码（null 为新建）</param>
        /// <returns>单据视图</returns>
        public static IBillView CreateNewBillView(Context ctx, string formId, object pkId = null)
        {
            FormMetadata meta = MetaDataServiceHelper.Load(ctx, formId) as FormMetadata;
            if (meta == null)
            {
                throw new Exception(string.Format("未能加载单据元数据，FormId={0}", formId));
            }

            BusinessInfo businessInfo = meta.BusinessInfo;
            var form = businessInfo.GetForm();

            IResourceServiceProvider formServiceProvider = form.GetFormServiceProvider(true);
            IDynamicFormViewService billViewService =
                formServiceProvider.GetService(typeof(IDynamicFormView)) as IDynamicFormViewService;

            BillOpenParameter openParam = new BillOpenParameter(form.Id, string.Empty);
            openParam.Context = ctx;
            openParam.ServiceName = form.FormServiceName;
            openParam.PageId = Guid.NewGuid().ToString();
            openParam.FormMetaData = meta;
            openParam.CreateFrom = CreateFrom.Default;
            openParam.ParentId = 0;
            openParam.GroupId = "";
            openParam.SetCustomParameter("ShowConfirmDialogWhenChangeOrg", false);

            List<AbstractDynamicFormPlugIn> plugs = form.CreateFormPlugIns();
            openParam.SetCustomParameter(FormConst.PlugIns, plugs);

            if (pkId != null)
            {
                openParam.Status = OperationStatus.EDIT;
                openParam.InitStatus = OperationStatus.EDIT;
                openParam.PkValue = pkId;
            }
            else
            {
                openParam.Status = OperationStatus.ADDNEW;
                openParam.PkValue = null;
            }

            billViewService.Initialize(openParam, formServiceProvider);

            IBillView view = (IBillView)billViewService;

            ((BillView)view).LoadData();

            return view;
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
