using System;
using System.Collections.Generic;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Authentication;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.WebApi.FormService;
using Kingdee.BOS.WebApi.ServicesStub;
using Kingdee.BOS.ServiceFacade.KDServiceFx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kingdee.CustLI.Business.PlugIn
{
    public class FeYBatchSave : AbstractWebApiBusinessService
    {
        public FeYBatchSave(KDServiceContext context) : base(context)
        {
        }

        public JObject ExecuteService(JObject request)
        {
            try
            {
                Context ctx = GetContext(request);
                if (ctx == null)
                {
                    return BuildResult(false, "金蝶登录失败，请检查用户名密码", 0, 0, new JArray());
                }

                string formId = request["FormId"]?.ToString();
                JArray dataList = request["DataList"] as JArray;

                if (string.IsNullOrEmpty(formId))
                {
                    return BuildResult(false, "FormId 不能为空", 0, 0, new JArray());
                }
                if (dataList == null || dataList.Count == 0)
                {
                    return BuildResult(false, "DataList 不能为空", 0, 0, new JArray());
                }

                string batchJson = BuildBatchSaveJson(formId, dataList);
                if (batchJson == null)
                {
                    return BuildResult(false, $"不支持的 FormId: {formId}", 0, 0, new JArray());
                }

                object rawResult = WebApiServiceCall.BatchSave(ctx, formId, batchJson);
                return MapBatchSaveResult(rawResult, dataList.Count);
            }
            catch (Exception ex)
            {
                return BuildResult(false, $"接口异常: {ex.Message}", 0, 0, new JArray());
            }
        }

        private Context GetContext(JObject request)
        {
            Context ctx = KDContext?.Session?.AppContext;
            if (ctx != null)
            {
                return ctx;
            }

            string userName = request["UserName"]?.ToString();
            string password = request["Password"]?.ToString();
            if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password))
            {
                try
                {
                    string dbId = GetDataCenterId();
                    if (string.IsNullOrEmpty(dbId))
                    {
                        return null;
                    }
                    var authService = new AuthService(KDContext);
                    var loginResult = authService.ValidateUser(dbId, userName, password, 2052);
                    if (loginResult != null && loginResult.LoginResultType == LoginResultType.Success)
                    {
                        return loginResult.Context;
                    }
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private string GetDataCenterId()
        {
            try
            {
                var webCtx = KDContext?.WebContext?.Context;
                if (webCtx != null)
                {
                    object dbId = webCtx.Items["dataCenterId"];
                    if (dbId != null)
                    {
                        return dbId.ToString();
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string BuildBatchSaveJson(string formId, JArray dataList)
        {
            JObject batchObj = new JObject();
            batchObj["NeedUpDateFields"] = new JArray();
            batchObj["IsDeleteEntry"] = "true";
            batchObj["SubSystemId"] = "";
            batchObj["IsVerifyBaseDataField"] = "false";
            batchObj["IsEntryBatchFill"] = "true";
            batchObj["ValidateFlag"] = "true";
            batchObj["NumberSearch"] = "true";
            batchObj["InterationFlags"] = "";

            JArray modelArr = new JArray();

            switch (formId)
            {
                case "PRD_INSTOCK":
                    foreach (var item in dataList)
                    {
                        modelArr.Add(BuildPrdInstockModel(item as JObject));
                    }
                    break;
                case "STK_TransferDirect_In":
                    foreach (var item in dataList)
                    {
                        modelArr.Add(BuildTransferInModel(item as JObject));
                    }
                    break;
                case "STK_TransferDirect_Out":
                    foreach (var item in dataList)
                    {
                        modelArr.Add(BuildTransferOutModel(item as JObject));
                    }
                    break;
                default:
                    return null;
            }

            batchObj["Model"] = modelArr;
            batchObj["BatchCount"] = Math.Min(dataList.Count, 5);
            return JsonConvert.SerializeObject(batchObj);
        }

        private JObject BuildPrdInstockModel(JObject item)
        {
            JObject model = new JObject();
            model["FID"] = 0;
            model["FBillTypeID"] = new JObject { ["FNUMBER"] = GetDefaultBillType("PRD_INSTOCK") };
            model["FStockOrgId"] = new JObject { ["FNumber"] = GetDefaultOrg() };
            model["FPrdOrgId"] = new JObject { ["FNumber"] = GetDefaultOrg() };
            model["FDate"] = DateTime.Now.ToString("yyyy-MM-dd");

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            entry["FEntryID"] = 0;
            entry["FMaterialId"] = new JObject { ["FNumber"] = item["FMaterialNumber"]?.ToString() };
            entry["FSrcBillNo"] = item["FSrcBillNo"]?.ToString();
            entry["FLot"] = item["FLot"]?.ToString();
            entry["FQty"] = Convert.ToDecimal(item["FQty"] ?? 0);
            entry["FStockId"] = new JObject { ["FNumber"] = item["FStockNumber"]?.ToString() };
            entryArr.Add(entry);

            model["FEntity"] = entryArr;
            return model;
        }

        private JObject BuildTransferInModel(JObject item)
        {
            JObject model = new JObject();
            model["FID"] = 0;
            model["FBillTypeID"] = new JObject { ["FNUMBER"] = GetDefaultBillType("STK_TransferDirect") };
            model["FStockOrgId"] = new JObject { ["FNumber"] = GetDefaultOrg() };
            model["FTransferDirect"] = "1";
            model["FDate"] = DateTime.Now.ToString("yyyy-MM-dd");

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            entry["FEntryID"] = 0;
            entry["FMaterialId"] = new JObject { ["FNumber"] = item["FMaterialNumber"]?.ToString() };
            entry["FSrcStockId"] = new JObject { ["FNumber"] = item["FSrcStockNumber"]?.ToString() };
            entry["FDestStockId"] = new JObject { ["FNumber"] = item["FDestStockNumber"]?.ToString() };
            entry["FLot"] = item["FLot"]?.ToString();
            entry["FQty"] = Convert.ToDecimal(item["FQty"] ?? 0);
            entryArr.Add(entry);

            model["FEntity"] = entryArr;
            return model;
        }

        private JObject BuildTransferOutModel(JObject item)
        {
            JObject model = new JObject();
            model["FID"] = 0;
            model["FBillTypeID"] = new JObject { ["FNUMBER"] = GetDefaultBillType("STK_TransferDirect") };
            model["FStockOrgId"] = new JObject { ["FNumber"] = GetDefaultOrg() };
            model["FTransferDirect"] = "2";
            model["FDate"] = DateTime.Now.ToString("yyyy-MM-dd");

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            entry["FEntryID"] = 0;
            entry["FMaterialId"] = new JObject { ["FNumber"] = item["FMaterialNumber"]?.ToString() };
            entry["FSrcStockId"] = new JObject { ["FNumber"] = item["FSrcStockNumber"]?.ToString() };
            entry["FDestStockId"] = new JObject { ["FNumber"] = "21" };
            entry["FLot"] = item["FLot"]?.ToString();
            entry["FQty"] = Convert.ToDecimal(item["FQty"] ?? 0);
            entryArr.Add(entry);

            model["FEntity"] = entryArr;
            return model;
        }

        private string GetDefaultOrg()
        {
            return "100";
        }

        private string GetDefaultBillType(string formId)
        {
            switch (formId)
            {
                case "PRD_INSTOCK":
                    return "SCRK01_SYS";
                case "STK_TransferDirect":
                    return "DBDL01_SYS";
                default:
                    return "";
            }
        }

        private JObject MapBatchSaveResult(object rawResult, int totalCount)
        {
            JObject result = BuildResult(true, "操作完成", totalCount, 0, new JArray());

            try
            {
                string jsonStr = JsonConvert.SerializeObject(rawResult);
                JObject rawJson = JObject.Parse(jsonStr);
                JObject responseStatus = rawJson["Result"]?["ResponseStatus"] as JObject;
                if (responseStatus == null)
                {
                    responseStatus = rawJson["ResponseStatus"] as JObject;
                }

                JArray details = new JArray();
                int successCount = 0;
                int failCount = 0;

                JArray successEntities = responseStatus?["SuccessEntities"] as JArray;
                JArray errors = responseStatus?["Errors"] as JArray;

                int maxItems = Math.Max(
                    successEntities?.Count ?? 0,
                    errors?.Count ?? 0
                );
                maxItems = Math.Max(maxItems, totalCount);

                for (int i = 0; i < maxItems; i++)
                {
                    JObject detail = new JObject();
                    detail["Index"] = i;
                    detail["Success"] = false;
                    detail["BillNo"] = "";
                    detail["Id"] = "";
                    detail["Message"] = "";

                    bool foundSuccess = false;
                    if (successEntities != null)
                    {
                        foreach (var se in successEntities)
                        {
                            if (se["DIndex"] != null && Convert.ToInt32(se["DIndex"]) == i)
                            {
                                detail["Success"] = true;
                                detail["BillNo"] = se["Number"]?.ToString() ?? "";
                                detail["Id"] = se["Id"]?.ToString() ?? "";
                                foundSuccess = true;
                                break;
                            }
                        }
                    }

                    if (!foundSuccess && errors != null)
                    {
                        foreach (var err in errors)
                        {
                            if (err["DIndex"] != null && Convert.ToInt32(err["DIndex"]) == i)
                            {
                                detail["Success"] = false;
                                detail["Message"] = err["Message"]?.ToString() ?? "";
                                break;
                            }
                        }
                    }

                    details.Add(detail);

                    if (detail["Success"]?.Value<bool>() == true)
                    {
                        successCount++;
                    }
                    else if (!string.IsNullOrEmpty(detail["Message"]?.ToString()))
                    {
                        failCount++;
                    }
                }

                result["Message"] = $"操作完成，成功{successCount}条，失败{failCount}条";
                result["Data"]["SuccessCount"] = successCount;
                result["Data"]["FailCount"] = failCount;
                result["Data"]["Details"] = details;
                result["Success"] = failCount == 0;
            }
            catch
            {
                result["Data"]["Details"] = new JArray();
            }

            return result;
        }

        private JObject BuildResult(bool success, string message, int successCount, int failCount, JArray details)
        {
            JObject result = new JObject();
            result["Success"] = success;
            result["Message"] = message;
            result["Data"] = new JObject();
            result["Data"]["SuccessCount"] = successCount;
            result["Data"]["FailCount"] = failCount;
            result["Data"]["Details"] = details;
            return result;
        }
    }
}
