/**
 * 飞亚货架对接 — 金蝶 WebAPI 批量保存接口
 *
 * ──────────────────────────────────────────
 * 一、业务接口地址（POST，Content-Type: application/json）
 * ──────────────────────────────────────────
 *   http://127.0.0.1/k3cloud/kingdee.CustLI.Business.PlugInWebApi.FeYBatchSave.ExecuteService,kingdee.CustLI.Business.PlugIn.common.kdsvc
 *
 *   请求体示例（生产入库单 — PRD_INSTOCK / 直接调拨单 — STK_TransferDirect_In/Out），整体包在 request 节点下：
 *   {
 *     "request": {
 *       "DBID": "6979e702b71b4c",
 *       "UserName": "kd01",
 *       "Password": "123qwe..",
 *       "FormId": "STK_TransferDirect_In",
 *       "DataList": [
 *         {
 *           "FMaterialNumber": "801147016108",
 *           "FSrcStockNumber": "CK001",
 *           "FDestStockNumber": "csTest",
 *           "FLot": "",
 *           "FQty": 1
 *         }
 *       ]
 *     }
 *   }
 *
 *   【DataList 字段说明】
 *   ┌──────────────────────┬────────────────────────────────────────────────────┬──────────┐
 *   │ 字段                 │ 适用单据                                            │ 必填     │
 *   ├──────────────────────┼────────────────────────────────────────────────────┼──────────┤
 *   │ FMaterialNumber      │ PRD_INSTOCK / STK_TransferDirect_In/Out           │ ✅       │
 *   │ FSrcBillNo           │ PRD_INSTOCK（生产订单号）                           │ ✅       │
 *   │ FSrcStockNumber      │ STK_TransferDirect_In/Out（调出仓库）              │ ✅       │
 *   │ FDestStockNumber     │ STK_TransferDirect_In/Out（调入仓库）              │ ✅       │
 *   │ FStockNumber         │ PRD_INSTOCK（仓库）                                │ ✅       │
 *   │ FLot                 │ 全部（批号）                                       │ ❌       │
 *   │ FQty                 │ 全部（数量）                                       │ ✅       │
 *   └──────────────────────┴────────────────────────────────────────────────────┴──────────┘
 *
 *   PRD_INSTOCK 请求体示例：
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
 *   STK_TransferDirect      - 直接调拨单（只保存不提交/审核）
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

using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceFacade.KDServiceFx;
using Kingdee.BOS.WebApi.ServicesStub;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.OpenXmlFormats.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    /// <summary>
/// 飞亚货架对接 WebAPI — 批量保存接口
/// 支持直接调拨单（STK_TransferDirect）和生产入库单（PRD_INSTOCK）的 BatchSave
    /// </summary>
    public class FeYBatchSave : AbstractWebApiBusinessService
    {
        // 金蝶 Cloud 本地地址
        private const string CloudUrl = "http://localhost/k3cloud/";

        // 登录凭据
        private string _dbId;
        private string _userName;
        private string _password;

        // 登录成功后保存 Cookie，后续请求携带此 Cookie 保持会话
        private CookieContainer _cookieContainer;

        public FeYBatchSave(KDServiceContext context)
            : base(context)
        {
        }

        /// <summary>
        /// WebAPI 统一入口
        /// 1. 拆包取出 request 节点
        /// 2. 参数校验（DBID/UserName/Password/FormId/DataList）
        /// 3. 登录鉴权
        /// 4. 校验 DataList 数据合法性
        /// 5. 根据 FormId 分发构建 BatchSave JSON
        /// 6. 调用金蝶 BatchSave 接口
        /// 7. 映射返回结果
        /// </summary>
        public JObject ExecuteService(JObject request)
        {
            try
            {
                // 请求体固定包在 request 节点下，拆包处理
                if (request["request"] != null)
                {
                    request = request["request"] as JObject ?? request;
                }

                // 提取登录参数
                _dbId = request["DBID"] != null ? request["DBID"].ToString() : "";
                _userName = request["UserName"] != null ? request["UserName"].ToString() : "";
                _password = request["Password"] != null ? request["Password"].ToString() : "";

                // 参数非空校验
                if (string.IsNullOrEmpty(_dbId))
                {
                    return BuildResult(false, "DBID(账套Id) 不能为空", 0, 0, new JArray());
                }
                if (string.IsNullOrEmpty(_userName) || string.IsNullOrEmpty(_password))
                {
                    return BuildResult(false, "UserName/Password 不能为空", 0, 0, new JArray());
                }

                // 登录鉴权
                if (!Login(_dbId, _userName, _password))
                {
                    return BuildResult(false, "账套登录鉴权失败，请检查 DBID/UserName/Password", 0, 0, new JArray());
                }

                // 提取业务参数
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

                // 校验 DataList 中每行数据的合法性（物料编号非空、数量>0）
                string qtyCheckMsg = ValidateDataList(dataList);
                if (!string.IsNullOrEmpty(qtyCheckMsg))
                {
                    return BuildResult(false, qtyCheckMsg, 0, 0, new JArray());
                }

                // 根据 FormId 构建 BatchSave 所需的 JSON Model 数组
                string batchJson = BuildBatchSaveJson(formId, dataList);
                if (batchJson == null)
                {
                    return BuildResult(false, "不支持的 FormId: " + formId, 0, 0, new JArray());
                }
                // 如果返回的不是 JSON 对象（以 { 开头），说明有校验错误信息直接返回
                if (!batchJson.TrimStart().StartsWith("{"))
                {
                    return BuildResult(false, batchJson, 0, 0, new JArray());
                }

                // 调用金蝶 BatchSave 接口（统一使用 STK_TransferDirect 作为单据标识）
                string apiFormId = "STK_TransferDirect";
                string rawResult = BatchSaveCall(apiFormId, batchJson);
                // 将金蝶原生返回结果映射为统一格式
                return MapBatchSaveResult(rawResult, dataList.Count);
            }
            catch (Exception ex)
            {
                return BuildResult(false, "接口异常: " + ex.Message, 0, 0, new JArray());
            }
        }

        /// <summary>
        /// 调用金蝶 AuthService.ValidateUser 做登录鉴权
        /// 登录成功后保存 CookieContainer 供后续请求使用
        /// </summary>
        /// <param name="dbId">账套 ID</param>
        /// <param name="userName">用户名</param>
        /// <param name="password">密码</param>
        /// <returns>true=登录成功</returns>
        private bool Login(string dbId, string userName, string password)
        {
            // 每次登录初始化新的 Cookie 容器
            _cookieContainer = new CookieContainer();

            // 金蝶登录鉴权接口地址
            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.AuthService.ValidateUser.common.kdsvc");

            // 参数列表：[dbId, userName, password, lcId]
            List<object> parameters = new List<object>();
            parameters.Add(dbId);
            parameters.Add(userName);
            parameters.Add(password);
            parameters.Add(2052);

            string content = JsonConvert.SerializeObject(parameters);

            try
            {
                string result = HttpPost(url, content);
                // LoginResultType=1 表示登录成功
                var iResult = JObject.Parse(result)["LoginResultType"].Value<int>();
                return iResult == 1;
            }
            catch (Exception e)
            {
                throw new Exception("登录鉴权异常: " + e.Message);
            }
        }

        /// <summary>
        /// 校验 DataList 中每行数据的合法性
        /// 规则：物料编号不能为空，数量必须 > 0
        /// </summary>
        /// <param name="dataList">待校验的数据列表</param>
        /// <returns>空字符串=全部通过，非空=错误信息</returns>
        private string ValidateDataList(JArray dataList)
        {
            List<string> errors = new List<string>();
            for (int i = 0; i < dataList.Count; i++)
            {
                JObject item = dataList[i] as JObject;
                if (item == null) continue;

                // 检查物料编号
                string materialNumber = item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : "";
                if (string.IsNullOrEmpty(materialNumber))
                {
                    errors.Add("第" + (i + 1) + "行 物料编号为空");
                    continue;
                }

                // 检查数量 <= 0
                decimal qty = ParseQty(item["FQty"]);
                if (qty <= 0)
                {
                    errors.Add("FMaterialNumber=" + materialNumber + " 的数量为0或无效");
                }
            }

            if (errors.Count == 0) return "";
            return "存在数量为0或无效的物料：" + string.Join("；", errors);
        }

        /// <summary>
        /// 安全解析数量值，转换失败返回 0
        /// </summary>
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

        /// <summary>
        /// 根据生产订单号+物料编码查询金蝶，获取 FID 和分录 FEntryId
        /// 用于生产入库单与上游生产订单建立关联
        /// </summary>
        /// <param name="moBillNo">生产订单号</param>
        /// <param name="materialNumber">物料编码</param>
        /// <returns>(fid, fentryId)，查不到返回 (0,0)</returns>
        private (long fid, long fentryId) GetMoIds(string moBillNo, string materialNumber)
        {
            // 金蝶 ExecuteBillQuery 接口地址
            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.ExecuteBillQuery.common.kdsvc");

            // SQL 注入转义：单引号替换为两个单引号
            string safeMoNo = (moBillNo ?? "").Replace("'", "''");
            string safeMat = (materialNumber ?? "").Replace("'", "''");

            // 组装查询参数
            JObject queryParam = new JObject();
            queryParam.Add("FormId", "PRD_MO");
            queryParam.Add("FieldKeys", "FID,FTreeEntity_FEntryID,FTreeEntity_FSeq");
            queryParam.Add("FilterString", "FBillNo = '" + safeMoNo + "' AND FMaterialId.FNumber = '" + safeMat + "'");
            queryParam.Add("OrderString", "FTreeEntity_FSeq ASC");
            queryParam.Add("TopRowCount", 0);
            queryParam.Add("StartRow", 0);
            queryParam.Add("Limit", 0);

            // 金蝶 ExecuteBillQuery 的请求参数格式：[JSON字符串]
            List<object> parameters = new List<object>();
            parameters.Add(JsonConvert.SerializeObject(queryParam));
            string rawResult = HttpPost(url, JsonConvert.SerializeObject(parameters));

            try
            {
                // 返回格式：[[fid, fentryId, fseq], ...]
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

        /// <summary>
        /// 调用金蝶 DynamicFormService.BatchSave 接口执行批量保存
        /// </summary>
        /// <param name="formId">单据标识（如 PRD_INSTOCK）</param>
        /// <param name="content">BatchSave 请求体 JSON 字符串</param>
        /// <returns>金蝶原生返回的 JSON 字符串</returns>
        private string BatchSaveCall(string formId, string content)
        {
            string url = string.Concat(CloudUrl, "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.BatchSave.common.kdsvc");
            // 金蝶 BatchSave 参数格式：[formId, contentJson]
            List<object> Parameters = new List<object>();
            Parameters.Add(formId);
            Parameters.Add(content);
            return HttpPost(url, JsonConvert.SerializeObject(Parameters));
        }

        /// <summary>
        /// 发起 HTTP POST 请求，附带 CookieContainer 保持登录会话
        /// 请求体按金蝶 WebAPI 格式包装：{format, useragent, rid, parameters, timestamp, v}
        /// </summary>
        /// <param name="url">请求地址</param>
        /// <param name="parametersJson">请求参数 JSON 字符串</param>
        /// <returns>响应内容</returns>
        private string HttpPost(string url, string parametersJson)
        {
            HttpWebRequest httpRequest = (HttpWebRequest)WebRequest.Create(url);
            httpRequest.Method = "POST";
            httpRequest.ContentType = "application/json";
            httpRequest.Timeout = 1000 * 60 * 10; // 10 分钟超时

            // 附加上一步登录获取的 Cookie
            if (_cookieContainer != null)
            {
                httpRequest.CookieContainer = _cookieContainer;
            }

            // 金蝶 WebAPI 要求的请求包装格式
            JObject jObj = new JObject();
            jObj.Add("format", 1);
            jObj.Add("useragent", "ApiClient");
            jObj.Add("rid", Guid.NewGuid().ToString().GetHashCode().ToString());
            jObj.Add("parameters", parametersJson);
            jObj.Add("timestamp", DateTime.Now);
            jObj.Add("v", "1.0");
            string sContent = jObj.ToString();

            // 写入请求体
            byte[] bytes = Encoding.UTF8.GetBytes(sContent);
            using (Stream reqStream = httpRequest.GetRequestStream())
            {
                reqStream.Write(bytes, 0, bytes.Length);
                reqStream.Flush();
            }

            // 读取响应
            using (var repStream = httpRequest.GetResponse().GetResponseStream())
            {
                using (var reader = new StreamReader(repStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// 根据 FormId 和数据列表构建 BatchSave 的完整 JSON 请求体
        /// 包含公共参数（NeedUpDateFields / IsDeleteEntry 等）和 Model 数组
        /// </summary>
        /// <param name="formId">单据标识</param>
        /// <param name="dataList">外部传入的数据列表</param>
        /// <returns>
        ///   JSON 字符串 — 构建成功；
        ///   null — FormId 不支持；
        ///   纯文本 — 校验错误信息（如生产订单不存在）
        /// </returns>
        private string BuildBatchSaveJson(string formId, JArray dataList)
        {
            // BatchSave 公共参数（所有单据类型共用）
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

            // Model 数组：每行数据对应一个 Model
            JArray modelArr = new JArray();

            switch (formId)
            {
                case "PRD_INSTOCK":
                    // 生产入库单：需要先根据生产订单号查询 MoId，再构建 Model
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
                case "STK_TransferDirect_Out":
                case "STK_TransferDirect":
                {
                    JObject model = BuildTransferDirectHeader();
                    JArray entryArr = new JArray();
                    foreach (var item in dataList)
                    {
                        entryArr.Add(BuildTransferDirectEntry(item as JObject));
                    }
                    model.Add("FBillEntry", entryArr);
                    modelArr.Add(model);
                }
                break;

                default:
                    // 不支持的 FormId
                    return null;
            }

            batchObj.Add("Model", modelArr);
            batchObj.Add("BatchCount", 5); // 每批 5 条
            return JsonConvert.SerializeObject(batchObj);
        }

        /// <summary>
        /// 构建生产入库单（PRD_INSTOCK）的 Model
        /// 关联上游生产订单，查询物料对应的默认车间
        /// </summary>
        private JObject BuildPrdInstockModel(JObject item)
        {
            // 从 DataList 行中提取字段
            string moBillNo = item["FSrcBillNo"]?.ToString() ?? "";
            string materialNumber = item["FMaterialNumber"]?.ToString() ?? "";
            decimal qty = ParseQty(item["FQty"]);
            string stockNumber = item["FStockNumber"]?.ToString() ?? "";
            string lot = item["FLot"]?.ToString() ?? "";

            // 查询上游生产订单的 FID 和分录 FEntryId
            var (moFid, moEntryId) = GetMoIds(moBillNo, materialNumber);

            // 查询物料对应的默认车间（FWorkShopId1）
            string workShopNumber = "";
            string sql = $@"SELECT d.FNUMBER AS FWORKSHOPNUMBER
                    FROM T_BD_MATERIAL a1
                    LEFT JOIN T_BD_MATERIALPRODUCE a2 ON a1.FMATERIALID = a2.FMATERIALID
                    LEFT JOIN T_BD_DEPARTMENT d ON a2.FWorkShopId = d.FDEPTID
                    WHERE a1.FNUMBER = '{materialNumber}'";
            DynamicObjectCollection conStr = DBUtils.ExecuteDynamicObject(this.KDContext.Session.AppContext, sql);
            if (conStr != null && conStr.Count > 0)
            {
                workShopNumber = conStr[0]["FWORKSHOPNUMBER"].ToString();
            }

            // 构建单据头
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

            // 构建分录（FEntity）
            JArray entryArr = new JArray();
            JObject entry = new JObject();

            // 上游生产订单关联字段
            AddField(entry, "FSrcEntryId", moEntryId);
            AddField(entry, "FMaterialId", Creat_JsonChildObject("FNumber", materialNumber));
            // 单位信息
            AddField(entry, "FUnitID", Creat_JsonChildObject("FNumber", "Pcs"));
            // 数量
            AddField(entry, "FMustQty", qty);
            AddField(entry, "FRealQty", qty);
            AddField(entry, "FCostRate", 100.0);
            // 基本单位数量
            AddField(entry, "FBaseUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            AddField(entry, "FBaseMustQty", qty);
            AddField(entry, "FBaseRealQty", qty);
            // 货主
            AddField(entry, "FOwnerTypeId", "BD_OwnerOrg");
            AddField(entry, "FOwnerId", Creat_JsonChildObject("FNumber", "100"));
            // 仓库
            AddField(entry, "FStockId", Creat_JsonChildObject("FNumber", stockNumber));
            // 车间（如果查询到有值才设置）
            if (!string.IsNullOrEmpty(workShopNumber))
            {
                AddField(entry, "FWorkShopId1", Creat_JsonChildObject("FNumber", workShopNumber));
            }
            // 上游生产订单信息
            AddField(entry, "FMoBillNo", moBillNo);
            AddField(entry, "FMoId", moFid);
            AddField(entry, "FMoEntryId", moEntryId);
            AddField(entry, "FMoEntrySeq", 1);
            // 库存单位数量
            AddField(entry, "FStockUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            AddField(entry, "FStockRealQty", qty);
            // 源单类型
            AddField(entry, "FSrcBillType", "PRD_MO");
            AddField(entry, "FSrcInterId", moFid);
            AddField(entry, "FSrcBillNo", moBillNo);
            AddField(entry, "FBasePrdRealQty", qty);
            // 库存状态
            AddField(entry, "FStockStatusId", Creat_JsonChildObject("FNumber", "KCZT01_SYS"));
            AddField(entry, "FSrcEntrySeq", 1);
            AddField(entry, "FMOMAINENTRYID", moEntryId);
            // 保管者
            AddField(entry, "FKeeperTypeId", "BD_KeeperOrg");
            AddField(entry, "FKeeperId", Creat_JsonChildObject("FNumber", "100"));
            // 批号
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

            // 分录关联信息（FEntity_Link）— 与上游生产订单建立上下游关联
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

        /// <summary>
        /// 为 JObject 添加字段（对象类型重载）
        /// 自动跳过 null / 空字符串 / 零值 / false 值，避免污染请求体
        /// </summary>
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

        /// <summary>
        /// 为 JObject 添加字段（JToken 类型重载）
        /// 自动跳过 null / 空字符串 JToken
        /// </summary>
        private void AddField(JObject obj, string key, JToken value)
        {
            if (value == null) return;
            if (value.Type == JTokenType.String && string.IsNullOrEmpty(value.ToString())) return;
            obj.Add(key, value);
        }

        /// <summary>
        /// 构建直接调拨单的单据头
        /// 字段映射见头注释
        /// </summary>
        private JObject BuildTransferDirectHeader()
        {
            string nowStr = DateTime.Now.ToString("yyyy-MM-dd 00:00:00");

            JObject model = new JObject();
            model.Add("FID", 0);
            model.Add("FBillTypeID", Creat_JsonChildObject("FNUMBER", "ZJDB01_SYS"));
            model.Add("FBizType", "NORMAL");
            model.Add("FTransferDirect", "GENERAL");
            model.Add("FTransferBizType", "InnerOrgTransfer");
            model.Add("FSettleOrgId", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FSaleOrgId", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FStockOutOrgId", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FOwnerTypeOutIdHead", "BD_OwnerOrg");
            model.Add("FOwnerOutIdHead", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FStockOrgId", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FIsIncludedTax", true);
            model.Add("FIsPriceExcludeTax", true);
            model.Add("FExchangeTypeId", Creat_JsonChildObject("FNUMBER", "HLTX01_SYS"));
            model.Add("FOwnerTypeIdHead", "BD_OwnerOrg");
            model.Add("FSETTLECURRID", Creat_JsonChildObject("FNUMBER", "PRE001"));
            model.Add("FExchangeRate", 1.0);
            model.Add("FOwnerIdHead", Creat_JsonChildObject("FNumber", "100"));
            model.Add("FDate", nowStr);
            model.Add("FBaseCurrId", Creat_JsonChildObject("FNumber", "PRE001"));
            model.Add("FWriteOffConsign", false);
            return model;
        }

        /// <summary>
        /// 构建直接调拨单的分录（FBillEntry）
        /// 字段映射：
        ///   FMaterialNumber  → FMaterialId.FNumber（物料）
        ///   FSrcStockNumber  → FSrcStockId.FNumber（调出仓库）
        ///   FDestStockNumber → FDestStockId.FNumber（调入仓库）
        ///   FQty             → FQty（数量）
        ///   FLot             → FLot.FNumber（批号，可选）
        /// </summary>
        private JObject BuildTransferDirectEntry(JObject item)
        {
            string materialNumber = item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : "";
            string srcStockNumber = item["FSrcStockNumber"] != null ? item["FSrcStockNumber"].ToString() : "";
            string destStockNumber = item["FDestStockNumber"] != null ? item["FDestStockNumber"].ToString() : "";
            string lot = item["FLot"] != null ? item["FLot"].ToString() : "";
            decimal qty = ParseQty(item["FQty"]);

            JObject entry = new JObject();
            entry.Add("FRowType", "Standard");
            entry.Add("FMaterialId", Creat_JsonChildObject("FNumber", materialNumber));
            entry.Add("FUnitID", Creat_JsonChildObject("FNumber", "Pcs"));
            entry.Add("FQty", qty);
            entry.Add("FSrcStockId", Creat_JsonChildObject("FNumber", srcStockNumber));
            entry.Add("FDestStockId", Creat_JsonChildObject("FNumber", destStockNumber));
            entry.Add("FSrcStockStatusId", Creat_JsonChildObject("FNumber", "KCZT01_SYS"));
            entry.Add("FDestStockStatusId", Creat_JsonChildObject("FNumber", "KCZT01_SYS"));
            entry.Add("FBusinessDate", DateTime.Now.ToString("yyyy-MM-dd 00:00:00"));
            entry.Add("FSrcBillTypeId", "");
            entry.Add("FOwnerTypeOutId", "BD_OwnerOrg");
            entry.Add("FOwnerOutId", Creat_JsonChildObject("FNumber", "100"));
            entry.Add("FOwnerTypeId", "BD_OwnerOrg");
            entry.Add("FOwnerId", Creat_JsonChildObject("FNumber", "100"));
            entry.Add("FSrcBillNo", "");
            entry.Add("FSecQty", 0.0);
            entry.Add("FExtAuxUnitQty", 0.0);
            entry.Add("FBaseUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            entry.Add("FBaseQty", qty);
            entry.Add("FISFREE", false);
            entry.Add("FKeeperTypeId", "BD_KeeperOrg");
            entry.Add("FActQty", 0.0);
            entry.Add("FKeeperId", Creat_JsonChildObject("FNumber", "100"));
            entry.Add("FKeeperTypeOutId", "BD_KeeperOrg");
            entry.Add("FKeeperOutId", Creat_JsonChildObject("FNumber", "100"));
            entry.Add("FDiscountRate", 0.0);
            entry.Add("FRepairQty", 0.0);
            entry.Add("FDestMaterialId", Creat_JsonChildObject("FNUMBER", materialNumber));
            entry.Add("FSaleUnitId", Creat_JsonChildObject("FNumber", "Pcs"));
            entry.Add("FSaleQty", qty);
            entry.Add("FSalBaseQty", qty);
            entry.Add("FPriceUnitID", Creat_JsonChildObject("FNumber", "Pcs"));
            entry.Add("FPriceQty", qty);
            entry.Add("FPriceBaseQty", qty);
            entry.Add("FOutJoinQty", 0.0);
            entry.Add("FBASEOUTJOINQTY", 0.0);
            entry.Add("FSOEntryId", 0);
            entry.Add("FTransReserveLink", false);
            entry.Add("FQmEntryId", 0);
            entry.Add("FConvertEntryId", 0);
            entry.Add("FCheckDelivery", false);
            entry.Add("FBomEntryId", 0);

            if (!string.IsNullOrEmpty(lot))
            {
                entry.Add("FLot", Creat_JsonChildObject("FNumber", lot));
            }

            return entry;
        }

        /// <summary>
        /// 获取默认库存组织编码（当前默认值：100）
        /// </summary>
        private string GetDefaultOrg()
        {
            return "100";
        }

        /// <summary>
        /// 根据 FormId 获取默认单据类型编码
        /// </summary>
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

        /// <summary>
        /// 将金蝶 BatchSave 返回的原生 JSON 映射为统一格式
        /// 返回格式：
        /// {
        ///   Success: true/false,
        ///   Message: "操作完成，成功N条，失败M条",
        ///   Data: {
        ///     SuccessCount: N,
        ///     FailCount: M,
        ///     Details: [{ Index, Success, BillNo, Id, Message }]
        ///   }
        /// }
        /// </summary>
        /// <param name="rawResult">金蝶原生返回的 JSON 字符串</param>
        /// <param name="totalCount">请求的总行数</param>
        private JObject MapBatchSaveResult(string rawResult, int totalCount)
        {
            JObject result = BuildResult(false, "操作未完成", 0, 0, new JArray());

            try
            {
                // 解析金蝶返回结果（处理多层数组嵌套）
                JObject rawJson = ResolveKdResponse(rawResult);
                if (rawJson == null)
                {
                    result["Message"] = "操作完成，成功0条，失败" + totalCount + "条";
                    JObject errDetail = new JObject();
                    errDetail["Index"] = 0;
                    errDetail["Success"] = false;
                    errDetail["BillNo"] = "";
                    errDetail["Id"] = "";
                    string snippet = rawResult != null && rawResult.Length > 800
                        ? rawResult.Substring(0, 800) + "......"
                        : rawResult;
                    errDetail["Message"] = "金蝶返回结果解析失败，原始返回：" + snippet;
                    ((JArray)result["Data"]["Details"]).Add(errDetail);
                    return result;
                }

                // 提取 ResponseStatus（兼容两种 JSON 层级结构）
                JObject responseStatus = rawJson["Result"] != null && rawJson["Result"]["ResponseStatus"] != null
                    ? rawJson["Result"]["ResponseStatus"] as JObject
                    : rawJson["ResponseStatus"] as JObject;

                JArray details = new JArray();
                int successCount = 0;
                int failCount = 0;

                // 整体是否成功
                bool isSuccess = responseStatus != null && responseStatus["IsSuccess"] != null
                    && responseStatus["IsSuccess"].Value<bool>();

                // 成功实体列表
                JArray successEntities = responseStatus != null && responseStatus["SuccessEntitys"] != null
                    ? responseStatus["SuccessEntitys"] as JArray
                    : new JArray();

                // 错误列表
                JArray errors = responseStatus != null && responseStatus["Errors"] != null
                    ? responseStatus["Errors"] as JArray
                    : new JArray();

                // 按最大索引遍历，确保每一行都有结果
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

                    // 先查找成功实体
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

                    // 未成功则取错误信息
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

                    // 统计成功/失败数
                    if (detail["Success"] != null && detail["Success"].Value<bool>())
                    {
                        successCount++;
                    }
                    else if (!string.IsNullOrEmpty(detail["Message"] != null ? detail["Message"].ToString() : ""))
                    {
                        failCount++;
                    }
                }

                // 组装最终结果
                result["Message"] = "操作完成，成功" + successCount + "条，失败" + failCount + "条";
                result["Data"]["SuccessCount"] = successCount;
                result["Data"]["FailCount"] = failCount;
                result["Data"]["Details"] = details;
                result["Success"] = isSuccess && failCount == 0;
            }
            catch (Exception mapEx)
            {
                // 映射异常时返回全部失败
                result["Message"] = "操作完成，成功0条，失败" + totalCount + "条";
                JObject errDetail2 = new JObject();
                errDetail2["Index"] = 0;
                errDetail2["Success"] = false;
                errDetail2["BillNo"] = "";
                errDetail2["Id"] = "";
                string snippet = rawResult != null && rawResult.Length > 800
                    ? rawResult.Substring(0, 800) + "......"
                    : rawResult;
                errDetail2["Message"] = "结果映射异常：" + mapEx.Message + " | 原始返回：" + snippet;
                result["Data"]["Details"] = new JArray(errDetail2);
            }

            return result;
        }

        /// <summary>
        /// 解析金蝶 HTTP 返回的原始 JSON
        /// 金蝶返回有时是 [[{...}]] 多层数组嵌套，统一提取为 JObject
        /// </summary>
        /// <param name="rawResult">原始返回字符串</param>
        /// <returns>解析后的 JObject，解析失败返回 null</returns>
        private JObject ResolveKdResponse(string rawResult)
        {
            if (string.IsNullOrEmpty(rawResult)) return null;

            try
            {
                // 如果是数组格式，逐层解包直到 JObject
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

        /// <summary>
        /// 构建统一返回格式
        /// </summary>
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

        /// <summary>
        /// 创建金蝶 JSON 子对象：{ "FNumber": "xxx" } 或 { "FNUMBER": "xxx" }
        /// 用于设置基础资料字段引用
        /// </summary>
        /// <param name="fckey">键名：FNumber 或 FNUMBER</param>
        /// <param name="fcval">编码值</param>
        private JObject Creat_JsonChildObject(string fckey, string fcval)
        {
            JObject cjitem = new JObject();
            cjitem.Add(fckey, fcval);
            return cjitem;
        }
    }
}
