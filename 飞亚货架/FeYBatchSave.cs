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
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    /// <summary>
    /// 飞亚货架对接 WebAPI — 批量保存接口
    /// 支持直接调拨单（STK_TransferDirect）和生产入库单（PRD_INSTOCK）的 BatchSave
    /// </summary>
    public partial class FeYBatchSave : AbstractWebApiBusinessService
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

                // 直接调拨单：前置校验物料是否在账套中存在
                if (formId == "STK_TransferDirect_In" || formId == "STK_TransferDirect_Out")
                {
                    List<string> materialNumbers = new List<string>();
                    foreach (var item in dataList)
                    {
                        string mat = item["FMaterialNumber"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(mat))
                            materialNumbers.Add(mat);
                    }

                    if (materialNumbers.Count > 0)
                    {
                        string safeNumbers = string.Join("','", materialNumbers.Select(m => m.Replace("'", "''")));
                        string sql = $"SELECT FNUMBER FROM T_BD_MATERIAL WHERE FNUMBER IN ('{safeNumbers}')";
                        var existList = DBUtils.ExecuteDynamicObject(this.KDContext.Session.AppContext, sql);

                        var existSet = new HashSet<string>(
                            existList.Select(r => r["FNUMBER"]?.ToString() ?? ""),
                            StringComparer.OrdinalIgnoreCase
                        );

                        var notExist = materialNumbers.Where(m => !existSet.Contains(m)).ToList();
                        if (notExist.Count > 0)
                        {
                            return BuildResult(false, "以下物料编码不存在: " + string.Join(", ", notExist), 0, 0, new JArray());
                        }
                    }
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
                return MapBatchSaveResult(rawResult, 1);
            }
            catch (Exception ex)
            {
                return BuildResult(false, "接口异常: " + ex.Message, 0, 0, new JArray());
            }
        }
    }
}
