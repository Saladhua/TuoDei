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
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Web.Bill;

namespace kingdee.CustLI.Business.PlugIn
{
    [System.ComponentModel.Description("林蓝汽车-物料审核同步包装方式到预置基础资料")]
    public class LinLanXQMatPackSyncServicePlugIn : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FNAME");
        }

        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            foreach (ExtendedDataEntity data in e.SelectedRows)
            {
                DynamicObject billObj = data.DataEntity;
                if (billObj == null) continue;

                long materialId = Convert.ToInt64(billObj["Id"]);
                if (materialId <= 0) continue;

                string materialName = ObjectToString(billObj["Name"]);
                DynamicObjectCollection packRows = QueryMaterialPackEntries(this.Context, materialId);

                SavePackRecords(this.Context, materialId, materialName, packRows);
            }
        }

        private DynamicObjectCollection QueryMaterialPackEntries(Context ctx, long materialId)
        {
            string sql = string.Format(
                @"SELECT F_CustLI_PackName, F_CustLI_PackLength, F_CustLI_PackWidth,
                          F_CustLI_PackHeight, F_CustLI_PackWeight, F_CustLI_PackDesc
                   FROM QSGA_t_Cust_Entry100006
                   WHERE FMATERIALID = {0}
                   ORDER BY FEntryID",
                materialId);

            var dbService = ServiceFactory.GetDBService(ctx);
            return dbService.ExecuteDynamicObject(ctx, sql);
        }

        private void SavePackRecords(Context ctx, long materialId, string materialName, DynamicObjectCollection packRows)
        {
            if (packRows == null || packRows.Count <= 0) return;

            var dbService = ServiceFactory.GetDBService(ctx);

            string existSql = string.Format(
                "SELECT FID, F_CustLI_PackName1 FROM T_BAS_PREBDONE WHERE F_CUSTLI_FMASTERID = {0}", materialId);
            DynamicObjectCollection existRows = dbService.ExecuteDynamicObject(ctx, existSql);

            Dictionary<string, long> existingMap = new Dictionary<string, long>();
            if (existRows != null)
            {
                foreach (DynamicObject row in existRows)
                {
                    string name = ObjectToString(row["F_CustLI_PackName1"]);
                    long fid = Convert.ToInt64(row["FID"]);
                    if (!string.IsNullOrEmpty(name))
                        existingMap[name] = fid;
                }
            }

            string datePrefix = DateTime.Now.ToString("yyyyMMdd");
            string maxSql = string.Format(
                "SELECT MAX(FNUMBER) AS FMAXNUM FROM T_BAS_PREBDONE WHERE FNUMBER LIKE '{0}%'", datePrefix);
            DynamicObjectCollection maxRows = dbService.ExecuteDynamicObject(ctx, maxSql);

            int sequence = 1;
            if (maxRows != null && maxRows.Count > 0 && maxRows[0]["FMAXNUM"] != null)
            {
                string maxNum = ObjectToString(maxRows[0]["FMAXNUM"]);
                if (maxNum.Length >= 4)
                {
                    string seqStr = maxNum.Substring(maxNum.Length - 4);
                    int.TryParse(seqStr, out sequence);
                    sequence++;
                }
            }

            foreach (DynamicObject packRow in packRows)
            {
                string packName = ObjectToString(packRow["F_CustLI_PackName"]);
                if (string.IsNullOrEmpty(packName)) continue;

                long? existingFid = null;
                if (existingMap.TryGetValue(packName, out long fid))
                    existingFid = fid;

                IBillView view = CreateNewBillView(ctx, "BAS_PreBaseDataOne", existingFid);

                DynamicFormViewPlugInProxy proxy = view.GetService<DynamicFormViewPlugInProxy>();
                proxy.FireOnLoad();

                view.Model.SetItemValueByID("F_CUSTLI_FMASTERID", materialId.ToString(), 0);
                view.InvokeFieldUpdateService("F_CUSTLI_FMASTERID", 0);
                view.Model.DataObject["Name"] = materialName;

                if (!existingFid.HasValue)
                {
                    string number = string.Format("{0}{1:D4}", datePrefix, sequence++);
                    view.Model.DataObject["Number"] = number;
                }

                view.Model.DataObject["F_CustLI_PackName1"] = ObjectToString(packRow["F_CustLI_PackName"]);
                view.Model.DataObject["F_CustLI_PackLength1"] = ObjectToDecimal(packRow["F_CustLI_PackLength"]);
                view.Model.DataObject["F_CustLI_PackWidth1"] = ObjectToDecimal(packRow["F_CustLI_PackWidth"]);
                view.Model.DataObject["F_CustLI_PackHeight1"] = ObjectToDecimal(packRow["F_CustLI_PackHeight"]);
                view.Model.DataObject["F_CustLI_PackWeight1"] = ObjectToDecimal(packRow["F_CustLI_PackWeight"]);
                view.Model.DataObject["F_CustLI_PackDesc1"] = ObjectToString(packRow["F_CustLI_PackDesc"]);

                IOperationResult saveResult = BusinessDataServiceHelper.Save(
                    ctx, view.BillBusinessInfo, view.Model.DataObject,
                    OperateOption.Create(), "Save");

                if (!saveResult.IsSuccess)
                {
                    var errMsgs = saveResult.ValidationErrors.Select(x => x.Message);
                    throw new Exception(
                        string.Format("BAS_PreBaseDataOne 保存失败：{0}", string.Join(",", errMsgs)));
                }

                long savedId = Convert.ToInt64(view.Model.DataObject["Id"]);
                object[] ids = new object[] { savedId };

                IOperationResult submitResult = BusinessDataServiceHelper.Submit(
                    ctx, view.BillBusinessInfo, ids, "Submit", OperateOption.Create());

                if (!submitResult.IsSuccess)
                {
                    var errMsgs = submitResult.ValidationErrors.Select(x => x.Message);
                    throw new Exception(
                        string.Format("BAS_PreBaseDataOne 提交失败：{0}", string.Join(",", errMsgs)));
                }

                IOperationResult auditResult = BusinessDataServiceHelper.Audit(
                    ctx, view.BillBusinessInfo, ids, OperateOption.Create());

                if (!auditResult.IsSuccess)
                {
                    var errMsgs = auditResult.ValidationErrors.Select(x => x.Message);
                    throw new Exception(
                        string.Format("BAS_PreBaseDataOne 审核失败：{0}", string.Join(",", errMsgs)));
                }
            }
        }

        private IBillView CreateNewBillView(Context ctx, string formId, object pkId = null)
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

        private string ObjectToString(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return value.ToString();
        }

        private decimal ObjectToDecimal(object value)
        {
            if (value == null || value == DBNull.Value) return 0m;
            decimal result;
            decimal.TryParse(value.ToString(), out result);
            return result;
        }
    }
}
