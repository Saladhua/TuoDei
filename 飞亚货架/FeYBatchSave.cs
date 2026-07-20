/**
 * 飞亚货架对接 — 金蝶 WebAPI 批量保存接口
 *
 * ──────────────────────────────────────────
 * 一、业务接口地址（POST，Content-Type: application/json）
 * ──────────────────────────────────────────
 *   http://127.0.0.1/k3cloud/kingdee.CustLI.Business.PlugInWebApi.FeYBatchSave.ExecuteService,kingdee.CustLI.Business.PlugIn.common.kdsvc
 *
 *   请求体示例（生产入库单 — PRD_INSTOCK），整体包在 request 节点下：
 *   {
 *     "request": {
 *       "DBID": "6979e702b71b4c",
 *       "UserName": "kd01",
 *       "Password": "123qwe..",
 *       "FormId": "PRD_INSTOCK",
 *       "DataList": [
 *         {
 *           "FMaterialNumber": "3.3.2.6.1",
 *           "FSrcBillNo": "MO2026070001",
 *           "FLot": "20260701-01",
 *           "FQty": 100,
 *           "FStockNumber": "19"
 *         },
 *         {
 *           "FMaterialNumber": "3.3.2.6.1",
 *           "FSrcBillNo": "MO2026070002",
 *           "FLot": "20260701-02",
 *           "FQty": 200,
 *           "FStockNumber": "20"
 *         }
 *       ]
 *     }
 *   }
 *
 *   说明：
 *   - 请求体固定包在 request 节点下（{ "request": { ... } }）
 *   - DBID（账套Id/DataCenterId）由外部请求体传入，接口先用 DBID+UserName+Password
 *     调用 AuthService.ValidateUser 登录鉴权，账套上下文通过登录 Cookie 透传至后续 BatchSave 调用
 *   - UserName / Password 为用户登录账号密码，鉴权失败时返回错误
 *   - DataList 中任一物料 FQty<=0 或 FMaterialNumber 为空时整体拦截，返回所有异常物料清单
 *
 * ──────────────────────────────────────────
 * 二、支持的单据类型（通过 FormId 区分）
 * ──────────────────────────────────────────
 *   PRD_INSTOCK             - 生产入库单（由生产订单汇总生成，只保存不提交/审核）
 *   STK_TransferDirect_In   - 直接调拨单（入库方向，只保存不提交/审核）
 *   STK_TransferDirect_Out  - 直接调拨单（出库方向，只保存不提交/审核）
 *
 * ──────────────────────────────────────────
 * 三、业务说明
 * ──────────────────────────────────────────
 *   - 所有单据只执行 BatchSave（保存），不提交/不审核
 *   - 生产入库单通过 FSrcBillNo（生产订单号）与上游生产订单自动关联
 *   - 仓库编码：19=货架仓，20=中转仓，21=货架出库仓
 *   - 默认库存组织：100
 *   - 本接口为自定义二开接口，不做热加载（HotUpdate）
 *
 * ──────────────────────────────────────────
 * 四、生产入库单 BatchSave 成功示例（供参考）
 * ──────────────────────────────────────────
 * {
 *   "NeedUpDateFields": [],
 *   "NeedReturnFields": [],
 *   "IsDeleteEntry": "true",
 *   "SubSystemId": "",
 *   "IsVerifyBaseDataField": "false",
 *   "IsEntryBatchFill": "true",
 *   "ValidateFlag": "true",
 *   "NumberSearch": "true",
 *   "IsAutoAdjustField": "false",
 *   "InterationFlags": "",
 *   "IgnoreInterationFlag": "",
 *   "IsControlPrecision": "false",
 *   "ValidateRepeatJson": "false",
 *   "Model": {
 *     "FBillType": { "FNUMBER": "SCRKD02_SYS" },
 *     "FDate": "2026-07-18 00:00:00",
 *     "FStockOrgId": { "FNumber": "100" },
 *     "FPrdOrgId": { "FNumber": "100" },
 *     "FOwnerTypeId0": "BD_OwnerOrg",
 *     "FOwnerId0": { "FNumber": "100" },
 *     "FCurrId": { "FNumber": "PRE001" },
 *     "FEntity": [{
 *       "FSrcEntryId": 100040,
 *       "FMaterialId": { "FNumber": "801147016108" },
 *       "FUnitID": { "FNumber": "Pcs" },
 *       "FMustQty": 10.0,
 *       "FRealQty": 10.0,
 *       "FCostRate": 100.0,
 *       "FBaseUnitId": { "FNumber": "Pcs" },
 *       "FBaseMustQty": 10.0,
 *       "FBaseRealQty": 10.0,
 *       "FOwnerTypeId": "BD_OwnerOrg",
 *       "FOwnerId": { "FNumber": "100" },
 *       "FStockId": { "FNumber": "csTest" },
 *       "FWorkShopId1": { "FNumber": "BM000004" },
 *       "FMoBillNo": "MO000017",
 *       "FMoId": 100023,
 *       "FMoEntryId": 100040,
 *       "FMoEntrySeq": 1,
 *       "FStockUnitId": { "FNumber": "Pcs" },
 *       "FStockRealQty": 10.0,
 *       "FSrcBillType": "PRD_MO",
 *       "FSrcInterId": 100023,
 *       "FSrcBillNo": "MO000017",
 *       "FBasePrdRealQty": 10.0,
 *       "FStockStatusId": { "FNumber": "KCZT01_SYS" },
 *       "FSrcEntrySeq": 1,
 *       "FMOMAINENTRYID": 100040,
 *       "FKeeperTypeId": "BD_KeeperOrg",
 *       "FKeeperId": { "FNumber": "100" },
 *       "FEntity_Link": [{
 *         "FEntity_Link_FRuleId": "PRD_MO2INSTOCK",
 *         "FEntity_Link_FSTableName": "T_PRD_MOENTRY",
 *         "FEntity_Link_FSBillId": "100023",
 *         "FEntity_Link_FSId": "100040"
 *       }]
 *     }]
 *   }
 * }
 *
 * 需求文档：加工区/飞亚货架对接/需求分析.md
 * 接口文档：加工区/飞亚货架对接/接口文档.md
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.ServiceFacade.KDServiceFx;
using Kingdee.BOS.WebApi.ServicesStub;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    public class FeYBatchSave : AbstractWebApiBusinessService
    {
        private const string CloudUrl = "http://localhost/k3cloud/";

        private string _dbId;
        private string _userName;
        private string _password;
        private CookieContainer _cookieContainer;

        public FeYBatchSave(KDServiceContext context)
            : base(context)
        {
        }

        public JObject ExecuteService(JObject request)
        {
            try
            {
                if (request["request"] != null)
                {
                    request = request["request"] as JObject ?? request;
                }

                _dbId = request["DBID"] != null ? request["DBID"].ToString() : "";
                _userName = request["UserName"] != null ? request["UserName"].ToString() : "";
                _password = request["Password"] != null ? request["Password"].ToString() : "";

                if (string.IsNullOrEmpty(_dbId))
                {
                    return BuildResult(false, "DBID(账套Id) 不能为空", 0, 0, new JArray());
                }
                if (string.IsNullOrEmpty(_userName) || string.IsNullOrEmpty(_password))
                {
                    return BuildResult(false, "UserName/Password 不能为空", 0, 0, new JArray());
                }

                if (!Login(_dbId, _userName, _password))
                {
                    return BuildResult(false, "账套登录鉴权失败，请检查 DBID/UserName/Password", 0, 0, new JArray());
                }

                string formId = request["FormId"] != null ? request["FormId"].ToString() : "";
                JArray dataList = request["DataList"] as JArray;

                if (string.IsNullOrEmpty(formId))
                {
                    return BuildResult(false, "FormId 不能为空", 0, 0, new JArray());
                }

                if (dataList == null || dataList.Count == 0)
                {
                    return BuildResult(false, "DataList 不能为空", 0, 0, new JArray());
                }

                string qtyCheckMsg = ValidateDataList(dataList);
                if (!string.IsNullOrEmpty(qtyCheckMsg))
                {
                    return BuildResult(false, qtyCheckMsg, 0, 0, new JArray());
                }

                string batchJson = BuildBatchSaveJson(formId, dataList);
                if (batchJson == null)
                {
                    return BuildResult(false, "不支持的 FormId: " + formId, 0, 0, new JArray());
                }
                if (!batchJson.TrimStart().StartsWith("{"))
                {
                    return BuildResult(false, batchJson, 0, 0, new JArray());
                }

                string rawResult = BatchSaveCall(formId, batchJson);
                return MapBatchSaveResult(rawResult, dataList.Count);
            }
            catch (Exception ex)
            {
                return BuildResult(false, "接口异常: " + ex.Message, 0, 0, new JArray());
            }
        }

        private bool Login(string dbId, string userName, string password)
        {
            _cookieContainer = new CookieContainer();

            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.AuthService.ValidateUser.common.kdsvc");
            List<object> parameters = new List<object>();
            parameters.Add(dbId);
            parameters.Add(userName);
            parameters.Add(password);
            parameters.Add(2052);
            string content = JsonConvert.SerializeObject(parameters);

            try
            {
                string result = HttpPost(url, content);
                var iResult = JObject.Parse(result)["LoginResultType"].Value<int>();

                return iResult == 1;
            }
            catch (Exception e)
            {
                throw new Exception("登录鉴权异常: " + e.Message);
            }
        }

        private string ValidateDataList(JArray dataList)
        {
            List<string> errors = new List<string>();
            for (int i = 0; i < dataList.Count; i++)
            {
                JObject item = dataList[i] as JObject;
                if (item == null) continue;

                string materialNumber = item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : "";
                if (string.IsNullOrEmpty(materialNumber))
                {
                    errors.Add("第" + (i + 1) + "行 物料编号为空");
                    continue;
                }

                decimal qty = ParseQty(item["FQty"]);
                if (qty <= 0)
                {
                    errors.Add("FMaterialNumber=" + materialNumber + " 的数量为0或无效");
                }
            }

            if (errors.Count == 0) return "";

            return "存在数量为0或无效的物料：" + string.Join("；", errors);
        }

        private decimal ParseQty(JToken qtyToken)
        {
            if (qtyToken == null) return 0m;
            decimal q;
            if (decimal.TryParse(qtyToken.ToString(), out q))
            {
                return q;
            }
            return 0m;
        }

        private (long fid, long fentryId) GetMoIds(string moBillNo, string materialNumber)
        {
            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.ExecuteBillQuery.common.kdsvc");

            string safeMoNo = (moBillNo ?? "").Replace("'", "''");
            string safeMat = (materialNumber ?? "").Replace("'", "''");

            JObject queryParam = new JObject();
            queryParam.Add("FormId", "PRD_MO");
            queryParam.Add("FieldKeys", "FID,FTreeEntity_FEntryID,FTreeEntity_FSeq");
            queryParam.Add("FilterString", "FBillNo = '" + safeMoNo + "' AND FMaterialId.FNumber = '" + safeMat + "'");
            queryParam.Add("OrderString", "FTreeEntity_FSeq ASC");
            queryParam.Add("TopRowCount", 0);
            queryParam.Add("StartRow", 0);
            queryParam.Add("Limit", 0);

            List<object> parameters = new List<object>();
            parameters.Add(JsonConvert.SerializeObject(queryParam));
            string rawResult = HttpPost(url, JsonConvert.SerializeObject(parameters));

            try
            {
                JArray rows = JArray.Parse(rawResult);
                if (rows == null || rows.Count == 0) return (0, 0);

                JArray first = rows[0] as JArray;
                if (first == null || first.Count < 2) return (0, 0);

                long fid = 0;
                long fentryId = 0;
                long.TryParse(first[0] != null ? first[0].ToString() : "", out fid);
                long.TryParse(first[1] != null ? first[1].ToString() : "", out fentryId);

                return (fid, fentryId);
            }
            catch
            {
                return (0, 0);
            }
        }

        private string BatchSaveCall(string formId, string content)
        {
            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.BatchSave.common.kdsvc");
            List<object> Parameters = new List<object>();
            Parameters.Add(formId);
            Parameters.Add(content);
            return HttpPost(url, JsonConvert.SerializeObject(Parameters));
        }

        private string HttpPost(string url, string parametersJson)
        {
            HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(url);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/json";
            httpRequest.Timeout = 1000 * 60 * 10;
            if (_cookieContainer != null)
            {
                httpRequest.CookieContainer = _cookieContainer;
            }

            JObject jObj = new JObject();
            jObj.Add("format", 1);
            jObj.Add("useragent", "ApiClient");
            jObj.Add("rid", Guid.NewGuid().ToString().GetHashCode().ToString());
            jObj.Add("parameters", parametersJson);
            jObj.Add("timestamp", DateTime.Now);
            jObj.Add("v", "1.0");
            string sContent = jObj.ToString();

            byte[] bytes = Encoding.UTF8.GetBytes(sContent);
            using (Stream reqStream = httpRequest.GetRequestStream())
            {
                reqStream.Write(bytes, 0, bytes.Length);
                reqStream.Flush();
            }

            using (var repStream = httpRequest.GetResponse().GetResponseStream())
            {
                using (var reader = new StreamReader(repStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private string BuildBatchSaveJson(string formId, JArray dataList)
        {
            JObject batchObj = new JObject();
            batchObj.Add("NeedUpDateFields", new JArray());
            batchObj.Add("NeedReturnFields", new JArray());
            batchObj.Add("IsDeleteEntry", "true");
            batchObj.Add("SubSystemId", "");
            batchObj.Add("IsVerifyBaseDataField", "false");
            batchObj.Add("IsEntryBatchFill", "true");
            batchObj.Add("ValidateFlag", "true");
            batchObj.Add("NumberSearch", "true");
            batchObj.Add("IsAutoAdjustField", "false");
            batchObj.Add("InterationFlags", "");
            batchObj.Add("IgnoreInterationFlag", "");
            batchObj.Add("IsControlPrecision", "false");
            batchObj.Add("ValidateRepeatJson", "false");

            JArray modelArr = new JArray();

            switch (formId)
            {
                case "PRD_INSTOCK":
                    foreach (var item in dataList)
                    {
                        JObject jo = item as JObject;
                        string moBillNo = jo != null && jo["FSrcBillNo"] != null ? jo["FSrcBillNo"].ToString() : "";
                        string materialNumber = jo != null && jo["FMaterialNumber"] != null ? jo["FMaterialNumber"].ToString() : "";
                        var (moFid, _) = GetMoIds(moBillNo, materialNumber);
                        if (moFid <= 0)
                        {
                            return "FSrcBillNo=" + moBillNo + "（物料 " + materialNumber + "）对应的生产订单在账套中不存在，无法生成生产入库单";
                        }
                        modelArr.Add(BuildPrdInstockModel(jo));
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

            batchObj.Add("Model", modelArr);
            batchObj.Add("BatchCount", 5);
            return JsonConvert.SerializeObject(batchObj);
        }

        private JObject BuildPrdInstockModel(JObject item)
        {
            string moBillNo = item["FSrcBillNo"]?.ToString() ?? "";
            string materialNumber = item["FMaterialNumber"]?.ToString() ?? "";
            decimal qty = ParseQty(item["FQty"]);
            string stockNumber = item["FStockNumber"]?.ToString() ?? "";
            string lot = item["FLot"]?.ToString() ?? "";

            var (moFid, moEntryId) = GetMoIds(moBillNo, materialNumber);

            JObject model = new JObject();
            AddField(model, "FBillType", Creat_JsonChildObject("FNUMBER", "SCRKD02_SYS"));
            AddField(model, "FDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AddField(model, "FStockOrgId", Creat_JsonChildObject("FNumber", "100"));
            AddField(model, "FPrdOrgId", Creat_JsonChildObject("FNumber", "100"));
            AddField(model, "FOwnerTypeId0", "BD_OwnerOrg");
            AddField(model, "FOwnerId0", Creat_JsonChildObject("FNumber", "100"));
            AddField(model, "FCurrId", Creat_JsonChildObject("FNumber", "PRE001"));
            model.Add("FIsEntrust", JToken.FromObject(false));
            model.Add("FEntrustInStockId", JToken.FromObject(0));

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            AddField(entry, "FSrcEntryId", moEntryId);
            AddField(entry, "FMaterialId", Creat_JsonChildObject("FNumber", materialNumber));
            AddField(entry, "FUnitID", Creat_JsonChildObject("FNumber", "Pcs"));
            AddField(entry, "FMustQty", qty);
            AddField(entry, "FRealQty", qty);
            AddField(entry, "FCostRate", 100.0);
            AddField(entry, "FBaseUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            AddField(entry, "FBaseMustQty", qty);
            AddField(entry, "FBaseRealQty", qty);
            AddField(entry, "FOwnerTypeId", "BD_OwnerOrg");
            AddField(entry, "FOwnerId", Creat_JsonChildObject("FNumber", "100"));
            AddField(entry, "FStockId", Creat_JsonChildObject("FNumber", stockNumber));
            AddField(entry, "FWorkShopId1", Creat_JsonChildObject("FNumber", "BM000004"));
            AddField(entry, "FMoBillNo", moBillNo);
            AddField(entry, "FMoId", moFid);
            AddField(entry, "FMoEntryId", moEntryId);
            AddField(entry, "FMoEntrySeq", 1);
            AddField(entry, "FStockUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            AddField(entry, "FStockRealQty", qty);
            AddField(entry, "FSrcBillType", "PRD_MO");
            AddField(entry, "FSrcInterId", moFid);
            AddField(entry, "FSrcBillNo", moBillNo);
            AddField(entry, "FBasePrdRealQty", qty);
            AddField(entry, "FStockStatusId", Creat_JsonChildObject("FNumber", "KCZT01_SYS"));
            AddField(entry, "FSrcEntrySeq", 1);
            AddField(entry, "FMOMAINENTRYID", moEntryId);
            AddField(entry, "FKeeperTypeId", "BD_KeeperOrg");
            AddField(entry, "FKeeperId", Creat_JsonChildObject("FNumber", "100"));
            AddField(entry, "FLot", Creat_JsonChildObject("FNumber", lot));
            entry.Add("FIsNew", JToken.FromObject(false));
            entry.Add("FCheckProduct", JToken.FromObject(false));
            AddField(entry, "FProductType", "1");
            AddField(entry, "FInStockType", "1");
            AddField(entry, "FISBACKFLUSH", true);
            entry.Add("FSecRealQty", JToken.FromObject(0.0));
            entry.Add("FIsFinished", JToken.FromObject(false));
            entry.Add("FSelReStkQty", JToken.FromObject(0.0));
            entry.Add("FBaseSelReStkQty", JToken.FromObject(0.0));
            entry.Add("FIsOverLegalOrg", JToken.FromObject(false));

            JArray linkArr = new JArray();
            JObject link = new JObject();
            link.Add("FEntity_Link_FRuleId", "PRD_MO2INSTOCK");
            link.Add("FEntity_Link_FSTableName", "T_PRD_MOENTRY");
            link.Add("FEntity_Link_FSBillId", moFid.ToString());
            link.Add("FEntity_Link_FSId", moEntryId.ToString());
            linkArr.Add(link);
            entry.Add("FEntity_Link", linkArr);

            entryArr.Add(entry);
            model.Add("FEntity", entryArr);
            return model;
        }

        private void AddField(JObject obj, string key, object value)
        {
            if (value == null) return;
            if (value is string s && string.IsNullOrEmpty(s)) return;
            if (value is int i && i == 0) return;
            if (value is long l && l == 0L) return;
            if (value is decimal d && d == 0m) return;
            if (value is double db && db == 0.0) return;
            if (value is bool b && !b) return;
            obj.Add(key, JToken.FromObject(value));
        }

        private void AddField(JObject obj, string key, JToken value)
        {
            if (value == null) return;
            if (value.Type == JTokenType.String && string.IsNullOrEmpty(value.ToString())) return;
            obj.Add(key, value);
        }

        private JObject BuildTransferInModel(JObject item)
        {
            JObject model = new JObject();
            model.Add("FID", 0);
            model.Add("FBillTypeID", Creat_JsonChildObject("FNUMBER", GetDefaultBillType("STK_TransferDirect")));
            model.Add("FStockOrgId", Creat_JsonChildObject("FNumber", GetDefaultOrg()));
            model.Add("FTransferDirect", "1");
            model.Add("FDate", DateTime.Now.ToString("yyyy-MM-dd"));

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            entry.Add("FEntryID", 0);
            entry.Add("FMaterialId", Creat_JsonChildObject("FNumber", item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : ""));
            entry.Add("FSrcStockId", Creat_JsonChildObject("FNumber", item["FSrcStockNumber"] != null ? item["FSrcStockNumber"].ToString() : ""));
            entry.Add("FDestStockId", Creat_JsonChildObject("FNumber", item["FDestStockNumber"] != null ? item["FDestStockNumber"].ToString() : ""));
            entry.Add("FLot", Creat_JsonChildObject("FNumber", item["FLot"] != null ? item["FLot"].ToString() : ""));
            entry.Add("FQty", ParseQty(item["FQty"]));
            entryArr.Add(entry);

            model.Add("FEntity", entryArr);
            return model;
        }

        private JObject BuildTransferOutModel(JObject item)
        {
            JObject model = new JObject();
            model.Add("FID", 0);
            model.Add("FBillTypeID", Creat_JsonChildObject("FNUMBER", GetDefaultBillType("STK_TransferDirect")));
            model.Add("FStockOrgId", Creat_JsonChildObject("FNumber", GetDefaultOrg()));
            model.Add("FTransferDirect", "2");
            model.Add("FDate", DateTime.Now.ToString("yyyy-MM-dd"));

            JArray entryArr = new JArray();
            JObject entry = new JObject();
            entry.Add("FEntryID", 0);
            entry.Add("FMaterialId", Creat_JsonChildObject("FNumber", item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : ""));
            entry.Add("FSrcStockId", Creat_JsonChildObject("FNumber", item["FSrcStockNumber"] != null ? item["FSrcStockNumber"].ToString() : ""));
            entry.Add("FDestStockId", Creat_JsonChildObject("FNumber", "21"));
            entry.Add("FLot", Creat_JsonChildObject("FNumber", item["FLot"] != null ? item["FLot"].ToString() : ""));
            entry.Add("FQty", ParseQty(item["FQty"]));
            entryArr.Add(entry);

            model.Add("FEntity", entryArr);
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

        private JObject MapBatchSaveResult(string rawResult, int totalCount)
        {
            JObject result = BuildResult(false, "操作未完成", 0, 0, new JArray());

            try
            {
                JObject rawJson = ResolveKdResponse(rawResult);
                if (rawJson == null)
                {
                    result["Message"] = "金蝶返回结果解析失败，原文：" + rawResult;
                    result["Data"]["RawResponse"] = rawResult;
                    return result;
                }

                JObject responseStatus = rawJson["Result"] != null && rawJson["Result"]["ResponseStatus"] != null
                    ? rawJson["Result"]["ResponseStatus"] as JObject
                    : rawJson["ResponseStatus"] as JObject;

                JArray details = new JArray();
                int successCount = 0;
                int failCount = 0;

                bool isSuccess = responseStatus != null && responseStatus["IsSuccess"] != null
                    && responseStatus["IsSuccess"].Value<bool>();

                JArray successEntities = responseStatus != null && responseStatus["SuccessEntities"] != null
                    ? responseStatus["SuccessEntities"] as JArray
                    : new JArray();

                JArray errors = responseStatus != null && responseStatus["Errors"] != null
                    ? responseStatus["Errors"] as JArray
                    : new JArray();

                List<string> errorMessages = new List<string>();
                foreach (var err in errors)
                {
                    string msg = err["Message"] != null ? err["Message"].ToString() : "";
                    if (!string.IsNullOrEmpty(msg)) errorMessages.Add(msg);
                }

                int maxItems = Math.Max(
                    successEntities != null ? successEntities.Count : 0,
                    errors != null ? errors.Count : 0
                );
                maxItems = Math.Max(maxItems, totalCount);

                for (int i = 0; i < maxItems; i++)
                {
                    JObject detail = new JObject();
                    detail.Add("Index", i);
                    detail.Add("Success", false);
                    detail.Add("BillNo", "");
                    detail.Add("Id", "");
                    detail.Add("Message", "");

                    bool foundSuccess = false;
                    if (successEntities != null)
                    {
                        foreach (var se in successEntities)
                        {
                            if (se["DIndex"] != null && se["DIndex"].Value<int>() == i)
                            {
                                detail["Success"] = true;
                                detail["BillNo"] = se["Number"] != null ? se["Number"].ToString() : "";
                                detail["Id"] = se["Id"] != null ? se["Id"].ToString() : "";
                                foundSuccess = true;
                                break;
                            }
                        }
                    }

                    if (!foundSuccess && errors != null)
                    {
                        foreach (var err in errors)
                        {
                            if (err["DIndex"] != null && err["DIndex"].Value<int>() == i)
                            {
                                detail["Message"] = err["Message"] != null ? err["Message"].ToString() : "";
                                break;
                            }
                        }
                    }

                    details.Add(detail);

                    if (detail["Success"] != null && detail["Success"].Value<bool>())
                    {
                        successCount++;
                    }
                    else if (!string.IsNullOrEmpty(detail["Message"] != null ? detail["Message"].ToString() : ""))
                    {
                        failCount++;
                    }
                }

                result["Message"] = (isSuccess ? "操作完成，成功" + successCount + "条" : "金蝶返回失败")
                    + (errorMessages.Count > 0 ? "：" + string.Join("；", errorMessages) : "");
                result["Data"]["SuccessCount"] = successCount;
                result["Data"]["FailCount"] = failCount;
                result["Data"]["Details"] = details;
                result["Success"] = isSuccess && failCount == 0;
                result["Data"]["RawResponse"] = rawResult;
            }
            catch (Exception mapEx)
            {
                result["Message"] = "结果映射异常：" + mapEx.Message;
                result["Data"]["RawResponse"] = rawResult;
                result["Data"]["Details"] = new JArray();
            }

            return result;
        }

        private JObject ResolveKdResponse(string rawResult)
        {
            if (string.IsNullOrEmpty(rawResult)) return null;

            try
            {
                if (rawResult.TrimStart().StartsWith("["))
                {
                    JArray arr = JArray.Parse(rawResult);
                    JToken inner = arr;
                    while (inner is JArray && ((JArray)inner).Count > 0)
                    {
                        inner = ((JArray)inner)[0];
                    }
                    if (inner is JObject) return inner as JObject;
                    return null;
                }
                return JObject.Parse(rawResult);
            }
            catch
            {
                return null;
            }
        }

        private JObject BuildResult(bool success, string message, int successCount, int failCount, JArray details)
        {
            JObject result = new JObject();
            result.Add("Success", success);
            result.Add("Message", message);
            result.Add("Data", new JObject());
            result["Data"]["SuccessCount"] = successCount;
            result["Data"]["FailCount"] = failCount;
            result["Data"]["Details"] = details;
            return result;
        }

        private JObject Creat_JsonChildObject(string fckey, string fcval)
        {
            JObject cjitem = new JObject();
            cjitem.Add(fckey, fcval);
            return cjitem;
        }
    }
}
