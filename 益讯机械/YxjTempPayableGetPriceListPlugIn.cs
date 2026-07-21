using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Bill;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.Operation;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using Kingdee.BOS.Web.Bill;
using Kingdee.K3.FIN.Core.ForCNConst;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace kingdee.CustLI.Business.PlugIn
{
    [Description("益讯机械-暂估应付单列表获取价目表价格"), HotUpdate]
    public class YxjTempPayableGetPriceListPlugIn : AbstractListPlugIn
    {
        private const string BarItemGetPrice = "tbGetPrice";
        private const string AcctTypeTemp = "2";
        private readonly HashSet<string> AllowDocStatus = new HashSet<string> { "Z", "A", "B", "D" };

        public override void BarItemClick(BarItemClickEventArgs e)
        {
            base.BarItemClick(e);


            if (e.BarItemKey != BarItemGetPrice)
                return;

            //按单据Id存储分录Id信息
            Dictionary<string, List<string>> bills = new Dictionary<string, List<string>>();
            foreach (ListSelectedRow item in this.ListView.SelectedRowsInfo)
            {
                var billId = item.FieldValues["FBillHead"].ToString();
                var billEntryId = item.EntryPrimaryKeyValue;

                if (bills.Keys.Contains(billId))
                {
                    bills[billId].Add(billEntryId);
                }
                else
                {
                    bills.Add(billId, new List<string> { billEntryId });
                }
            }

            foreach (var item in bills)
            {
                //根据该Id 创建表单视图
                var view = CreateBillView(this.Context, "AP_Payable", null, item.Key);

                //读取明细行数据
                var entryCount = view.Model.GetEntryRowCount("FEntityDetail");
                for (int i = 0; i < entryCount; i++)
                {
                    var entryId = view.Model.GetEntryPKValue("FEntityDetail", i).ToString();
                    //查找选中行 ben
                    if (!item.Value.Any(x => x.Contains(entryId)))
                    {
                        continue;
                    }
                    var matObj = view.Model.GetValue("FMATERIALID", i) as DynamicObject;
                    if (matObj == null)
                    {
                        continue;
                    }
                    long materialId = Convert.ToInt64(matObj["Id"]);

                    var supplierObj = view.Model.GetValue("FSUPPLIERID") as DynamicObject;
                    if (supplierObj == null)
                    {
                        continue;
                    }
                    long supplierId = Convert.ToInt64(supplierObj["Id"]);

                    string sql = $@"
                        SELECT a.FTAXPRICE
                        FROM t_PUR_PriceListEntry a
                        INNER JOIN t_PUR_PriceList b ON a.FID = b.FID
                        WHERE a.FMATERIALID = {materialId}
                          AND b.FSUPPLIERID = {supplierId}
                          AND a.FDISABLESTATUS <> 'A'
                          AND a.FEFFECTIVEDATE <= GETDATE()
                        ORDER BY a.FEFFECTIVEDATE DESC";

                    var priceObjs = DBUtils.ExecuteDynamicObject(this.Context, sql);
                    decimal taxPrice = 0m;
                    if (priceObjs != null && priceObjs.Count > 0 && priceObjs[0]["FTAXPRICE"] != DBNull.Value)
                    {
                        taxPrice = Convert.ToDecimal(priceObjs[0]["FTAXPRICE"]);
                    }

                    view.Model.SetValue("FTAXPRICE", taxPrice, i);
                    view.InvokeFieldUpdateService("FTAXPRICE", i);
                }
                //这里调用保存
                view.Model.Save();
            }
        }

        /// <summary>
        /// 创建单据视图
        /// </summary>
        /// <param name="ctx">上下文</param>
        /// <param name="formId">单据类型ID</param>
        /// <param name="layoutId">布局ID</param>
        /// <param name="pkId">主键ID</param>
        /// <returns></returns>

        private static BillView CreateBillView(Context ctx, string formId, string layoutId = null, object pkId = null)
        {
            var meta = (FormMetadata)Kingdee.BOS.ServiceHelper.MetaDataServiceHelper.Load(ctx, formId); //单据唯一标识
            var form = meta.BusinessInfo.GetForm();

            var param = new BillOpenParameter(formId, layoutId);
            param.Context = ctx;
            param.FormMetaData = meta;
            if (pkId != null && !string.IsNullOrWhiteSpace(pkId.ToString()))
            {
                param.Status = OperationStatus.EDIT;
                param.InitStatus = OperationStatus.EDIT;
                param.PkValue = pkId; //单据主键内码FID
            }
            else
            {
                param.Status = OperationStatus.ADDNEW;
                param.InitStatus = OperationStatus.ADDNEW;
            }

            param.SetCustomParameter("formID", form.Id);
            param.SetCustomParameter("PlugIns", form.CreateFormPlugIns()); //插件实例模型
            param.SetCustomParameter("ShowConfirmDialogWhenChangeOrg", false);
            param.NetCtrlDisable = true; // 禁用网控
            var provider = form.GetFormServiceProvider();
            var billview = (BillView)provider.GetService(typeof(IDynamicFormView));
            //var type = Type.GetType("Kingdee.BOS.Web.Import.ImportBillView,Kingdee.BOS.Web");
            //var billview2 = (BillView)Activator.CreateInstance(type);
            billview.Initialize(param, provider); //初始化                
            billview.LoadData(); //加载单据数据                
            return billview;
        }
    }
}
