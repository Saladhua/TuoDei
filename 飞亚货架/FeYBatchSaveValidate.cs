/// 校验 & 查询（ValidateDataList、BatchQueryUnitDict、ParseQty）
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
        /// 批量查询物料的基本单位、库存单位、销售单位
        /// </summary>
        private Dictionary<string, UnitInfo> BatchQueryUnitDict(List<string> materialNumbers)
        {
            var result = new Dictionary<string, UnitInfo>(StringComparer.OrdinalIgnoreCase);
            if (materialNumbers == null || materialNumbers.Count == 0) return result;

            var distinctMats = materialNumbers.Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
            if (distinctMats.Count == 0) return result;

            string safeNumbers = string.Join("','", distinctMats.Select(m => m.Replace("'", "''")));
            string sql = $@"SELECT
    m.FNUMBER,
    u1.FNUMBER AS FBaseUnitNumber,
    u2.FNUMBER AS FStoreUnitNumber,
    u3.FNUMBER AS FSaleUnitNumber
FROM T_BD_MATERIAL m
LEFT JOIN T_BD_MATERIALBASE mb ON m.FMATERIALID = mb.FMATERIALID
LEFT JOIN T_BD_MATERIALSTOCK ms ON m.FMATERIALID = ms.FMATERIALID
LEFT JOIN T_BD_MATERIALSALE ms2 ON m.FMATERIALID = ms2.FMATERIALID
LEFT JOIN T_BD_UNIT u1 ON mb.FBASEUNITID = u1.FUnitID
LEFT JOIN T_BD_UNIT u2 ON ms.FSTOREUNITID = u2.FUnitID
LEFT JOIN T_BD_UNIT u3 ON ms2.FSALEUNITID = u3.FUnitID
WHERE m.FNUMBER IN ('{safeNumbers}')";

            try
            {
                var rows = DBUtils.ExecuteDynamicObject(this.KDContext.Session.AppContext, sql);
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        string fn = row["FNUMBER"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(fn)) continue;
                        result[fn] = new UnitInfo
                        {
                            BaseUnitNumber = row["FBaseUnitNumber"]?.ToString() ?? "",
                            StoreUnitNumber = row["FStoreUnitNumber"]?.ToString() ?? "",
                            SaleUnitNumber = row["FSaleUnitNumber"]?.ToString() ?? ""
                        };
                    }
                }
            }
            catch
            {
            }

            return result;
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
    }
}
