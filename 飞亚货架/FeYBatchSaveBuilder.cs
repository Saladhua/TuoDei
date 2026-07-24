/// JSON 构建（BuildBatchSaveJson、BuildPrdInstockModel、BuildTransferDirectHeader、BuildTransferDirectEntry、GetDefaultOrg、GetDefaultBillType、Creat_JsonChildObject、AddField）
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Orm.DataEntity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    public partial class FeYBatchSave
    {
        private readonly Dictionary<string, string> _stockLocSuffixCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

            // 收集所有物料编码，批量查询单位信息
            List<string> allMatNumbers = new List<string>();
            foreach (var item in dataList)
            {
                JObject jo = item as JObject;
                string mat = jo != null && jo["FMaterialNumber"] != null ? jo["FMaterialNumber"].ToString() : "";
                if (!string.IsNullOrEmpty(mat))
                    allMatNumbers.Add(mat);
            }
            var unitDict = BatchQueryUnitDict(allMatNumbers);

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
                        modelArr.Add(BuildPrdInstockModel(jo, unitDict));
                    }
                    break;

                case "STK_TransferDirect_In":
                {
                    JObject model = BuildTransferDirectHeader();
                    JArray entryArr = new JArray();
                    foreach (var item in dataList)
                    {
                        entryArr.Add(BuildTransferDirectEntry(item as JObject, unitDict));
                    }
                    model.Add("FBillEntry", entryArr);
                    modelArr.Add(model);
                }
                break;

                case "STK_TransferDirect_Out":
                {
                    JObject model = BuildTransferDirectHeader();
                    JArray entryArr = new JArray();
                    foreach (var item in dataList)
                    {
                        entryArr.Add(BuildTransferDirectEntry(item as JObject, unitDict));
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
        private JObject BuildPrdInstockModel(JObject item, Dictionary<string, UnitInfo> unitDict)
        {
            // 从 DataList 行中提取字段
            string moBillNo = item["FSrcBillNo"]?.ToString() ?? "";
            string materialNumber = item["FMaterialNumber"]?.ToString() ?? "";
            decimal qty = ParseQty(item["FQty"]);
            string stockNumber = item["FStockNumber"]?.ToString() ?? "";
            string lot = item["FLot"]?.ToString() ?? "";
            string stockLocId = item["FStockLocId"]?.ToString() ?? "";

            // 从单位字典中取当前物料的单位
            UnitInfo unitInfo = unitDict != null && unitDict.ContainsKey(materialNumber) ? unitDict[materialNumber] : null;
            string baseUnit = unitInfo != null && !string.IsNullOrEmpty(unitInfo.BaseUnitNumber) ? unitInfo.BaseUnitNumber : "Pcs";
            string storeUnit = unitInfo != null && !string.IsNullOrEmpty(unitInfo.StoreUnitNumber) ? unitInfo.StoreUnitNumber : baseUnit;

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
            AddField(entry, "FUnitID", Creat_JsonChildObject("FNumber", baseUnit));
            // 数量
            AddField(entry, "FMustQty", qty);
            AddField(entry, "FRealQty", qty);
            AddField(entry, "FCostRate", 100.0);
            // 基本单位数量
            AddField(entry, "FBaseUnitId", Creat_JsonChildObject("FNumber", baseUnit));
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
            AddField(entry, "FStockUnitId", Creat_JsonChildObject("FNumber", storeUnit));
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
            // 批号（为空时不发送）
            if (!string.IsNullOrEmpty(lot))
            {
                AddField(entry, "FLot", Creat_JsonChildObject("FNumber", lot));
            }
            // 仓位（为空时不发送）
            if (!string.IsNullOrEmpty(stockLocId))
            {
                string suffix = GetStockLocFieldSuffix(stockNumber);
                if (!string.IsNullOrEmpty(suffix))
                {
                    JObject stockLocObj = new JObject();
                    JObject innerLoc = new JObject();
                    innerLoc.Add("FNumber", stockLocId);
                    stockLocObj.Add("FSTOCKLOCID__" + suffix, innerLoc);
                    entry.Add("FStockLocId", stockLocObj);
                }
            }
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
        private JObject BuildTransferDirectEntry(JObject item, Dictionary<string, UnitInfo> unitDict)
        {
            string materialNumber = item["FMaterialNumber"] != null ? item["FMaterialNumber"].ToString() : "";
            string srcStockNumber = item["FSrcStockNumber"] != null ? item["FSrcStockNumber"].ToString() : "";
            string destStockNumber = item["FDestStockNumber"] != null ? item["FDestStockNumber"].ToString() : "";
            string lot = item["FLot"] != null ? item["FLot"].ToString() : "";
            string srcStockLocId = item["FSrcStockLocId"]?.ToString() ?? "";
            string destStockLocId = item["FDestStockLocId"]?.ToString() ?? "";
            decimal qty = ParseQty(item["FQty"]);

            UnitInfo unitInfo = unitDict != null && unitDict.ContainsKey(materialNumber) ? unitDict[materialNumber] : null;
            string baseUnit = unitInfo != null && !string.IsNullOrEmpty(unitInfo.BaseUnitNumber) ? unitInfo.BaseUnitNumber : "Pcs";
            string saleUnit = unitInfo != null && !string.IsNullOrEmpty(unitInfo.SaleUnitNumber) ? unitInfo.SaleUnitNumber : baseUnit;

            JObject entry = new JObject();
            entry.Add("FRowType", "Standard");
            entry.Add("FMaterialId", Creat_JsonChildObject("FNumber", materialNumber));
            entry.Add("FUnitID", Creat_JsonChildObject("FNumber", baseUnit));
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
            entry.Add("FBaseUnitId", Creat_JsonChildObject("FNumber", baseUnit));
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
            entry.Add("FSaleUnitId", Creat_JsonChildObject("FNumber", saleUnit));
            entry.Add("FSaleQty", qty);
            entry.Add("FSalBaseQty", qty);
            entry.Add("FPriceUnitID", Creat_JsonChildObject("FNumber", baseUnit));
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

            // 调出仓位：入参有仓位值才查 FFLEXID 拼动态字段名，查不到则跳过
            if (!string.IsNullOrEmpty(srcStockLocId))
            {
                string srcSuffix = GetStockLocFieldSuffix(srcStockNumber);
                if (!string.IsNullOrEmpty(srcSuffix))
                {
                    JObject srcLocObj = new JObject();
                    JObject innerLoc = new JObject();
                    innerLoc.Add("FNumber", srcStockLocId);
                    srcLocObj.Add("FSRCSTOCKLOCID__" + srcSuffix, innerLoc);
                    entry.Add("FSrcStockLocId", srcLocObj);
                }
            }
            // 调入仓位：同上调出仓位逻辑，用调入仓库编码查
            if (!string.IsNullOrEmpty(destStockLocId))
            {
                string destSuffix = GetStockLocFieldSuffix(destStockNumber);
                if (!string.IsNullOrEmpty(destSuffix))
                {
                    JObject destLocObj = new JObject();
                    JObject innerLoc = new JObject();
                    innerLoc.Add("FNumber", destStockLocId);
                    destLocObj.Add("FDESTSTOCKLOCID__" + destSuffix, innerLoc);
                    entry.Add("FDestStockLocId", destLocObj);
                }
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
        /// 根据仓库编码查询仓位值集维度内码（FFLEXID）
        /// 返回 "FF{FFLEXID}" 后缀，用于拼写仓位字段名
        /// 带缓存，同一仓库只查一次
        /// </summary>
        private string GetStockLocFieldSuffix(string stockNumber)
        {
            if (string.IsNullOrEmpty(stockNumber)) return "";

            if (_stockLocSuffixCache.ContainsKey(stockNumber))
                return _stockLocSuffixCache[stockNumber];

            string sql = $@"SELECT T1.FFLEXID
FROM T_BD_STOCKFLEXITEM T1
JOIN T_BD_STOCK T2 ON T1.FSTOCKID = T2.FSTOCKID
WHERE T2.FNUMBER = '{stockNumber}'";

            try
            {
                DynamicObjectCollection conStr = DBUtils.ExecuteDynamicObject(this.KDContext.Session.AppContext, sql);
                if (conStr != null && conStr.Count > 0)
                {
                    string suffix = "FF" + conStr[0]["FFLEXID"].ToString();
                    _stockLocSuffixCache[stockNumber] = suffix;
                    return suffix;
                }
            }
            catch
            {
            }

            _stockLocSuffixCache[stockNumber] = "";
            return "";
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
