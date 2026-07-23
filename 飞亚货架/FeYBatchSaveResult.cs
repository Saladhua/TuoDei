/// 结果映射（MapBatchSaveResult、ResolveKdResponse、BuildResult）
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    public partial class FeYBatchSave
    {
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

                    // 未成功且无错误信息 — 兜底标记
                    if (!foundSuccess && string.IsNullOrEmpty(detail["Message"].ToString()))
                    {
                        detail["Message"] = "金蝶未返回该条记录的处理结果";
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
    }
}
