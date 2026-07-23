/// HTTP 通信（Login、HttpPost、BatchSaveCall、GetMoIds）
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Orm.DataEntity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    public partial class FeYBatchSave
    {
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
    }
}
