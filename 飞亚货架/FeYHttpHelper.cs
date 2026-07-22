using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace kingdee.CustLI.Business.PlugIn
{
    internal static class FeYHttpHelper
    {
        private const string ConfigFilePath = @"D:\kingdee\CustAppConfig\App.config";
        private const string ConfigKey_Url = "FeY_MaterialPushUrl";

        internal static string GetPushUrl()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return "";
            }

            var configMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = ConfigFilePath
            };
            var config = ConfigurationManager.OpenMappedExeConfiguration(
                configMap, ConfigurationUserLevel.None);
            var setting = config.AppSettings.Settings[ConfigKey_Url];
            return setting?.Value?.TrimEnd('/') ?? "";
        }

        internal static (bool Success, string Message) PushMaterial(
            string materialCode,
            string materialName,
            string materialSpec,
            string materialUnit,
            string materialType)
        {
            string url = GetPushUrl();
            if (string.IsNullOrEmpty(url))
            {
                return (false, "飞亚物料接收接口地址未配置（AppSettings FeY_MaterialPushUrl）");
            }

            JObject body = new JObject();
            body["MaterialCode"] = materialCode ?? "";
            body["MaterialName"] = materialName ?? "";
            body["MaterialSpec"] = materialSpec ?? "";
            body["MaterailUnit"] = materialUnit ?? "";
            body["MaterailType"] = materialType ?? "";

            string jsonBody = body.ToString(Formatting.None);

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = 30000;

                byte[] data = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = data.Length;

                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string result = reader.ReadToEnd();

                    try
                    {
                        JObject respObj = JObject.Parse(result);
                        int code = respObj["code"]?.Value<int>() ?? 0;
                        string msg = respObj["message"]?.ToString() ?? "";
                        return (code == 200, msg);
                    }
                    catch
                    {
                        return (true, result);
                    }
                }
            }
            catch (WebException ex)
            {
                string errorMsg = "HTTP请求异常";
                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        errorMsg += ": " + reader.ReadToEnd();
                    }
                }
                else
                {
                    errorMsg += ": " + ex.Message;
                }
                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, "推送异常: " + ex.Message);
            }
        }
    }
}
