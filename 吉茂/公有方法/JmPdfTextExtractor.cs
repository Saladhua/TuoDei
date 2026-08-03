using System;
using System.Collections.Generic;
using System.Text;
using iTextSharp.text.pdf;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 吉茂-销售订单导入 · 采购合同PDF内容流文本提取器
    ///
    /// 背景：样例PDF的工艺字段用 STSong-Light Type0 字体（UniGB-UTF16-H、无 ToUnicode CMap）绘制，
    /// iTextSharp PdfTextExtractor 对无 ToUnicode 的 Type0 字体输出空白，导致工艺值
    /// （组装图、接口等 ASCII 值）全部丢失。经逐字节验证，内容流原始字节含全部关键 ASCII 信息。
    ///
    /// 本类直接读取页面内容流原始字节，按 Tf 当前字体区分解码：
    ///   - Type0 字体（STSong-Light）：UTF-16BE 解码。该字体下 ASCII 字符高字节为 0，
    ///     低字节即字符本身，可完整还原值；中文标签无 ToUnicode 无法还原，统一转为空格。
    ///   - WinAnsi 字体（表头/表格行）：CP1252 单字节解码。
    ///
    /// 输出：按 Tj/TJ/'/" 操作符切分的解码文本片段（保留原始顺序），供 JmPdfParseHelper 关键字正则解析。
    /// </summary>
    public static class JmPdfTextExtractor
    {
        /// <summary>
        /// 提取 PDF 全部页面的文本片段（按内容流操作符切分，保留顺序）。
        /// </summary>
        /// <param name="pdfBytes">PDF 文件字节</param>
        /// <returns>解码后的文本片段列表；解析失败时返回空列表</returns>
        public static List<string> ExtractFragments(byte[] pdfBytes)
        {
            List<string> result = new List<string>();
            if (pdfBytes == null || pdfBytes.Length == 0) return result;

            PdfReader reader = null;
            try
            {
                reader = new PdfReader(pdfBytes);
                for (int page = 1; page <= reader.NumberOfPages; page++)
                {
                    // 当前页字体字典（判断字体是否 Type0），映射：字体名 -> 是否 Type0
                    Dictionary<string, bool> fontType0Map = BuildFontType0Map(reader, page);

                    byte[] content = reader.GetPageContent(page);
                    if (content == null || content.Length == 0) continue;

                    // 用 PRTokeniser + PdfContentParser 逐操作符解析内容流
                    PRTokeniser tokeniser = new PRTokeniser(new RandomAccessFileOrArray(content));
                    PdfContentParser parser = new PdfContentParser(tokeniser);

                    string currentFont = "";
                    List<PdfObject> objs = new List<PdfObject>();
                    while (true)
                    {
                        objs.Clear();
                        List<PdfObject> parsed = parser.Parse(objs);
                        if (parsed == null || parsed.Count == 0) break;

                        // 操作符位于解析结果最后一项，类型为 PdfLiteral
                        PdfObject op = parsed[parsed.Count - 1];
                        if (!(op is PdfLiteral)) continue;
                        string token = op.ToString();

                        if (token == "Tf")
                        {
                            // 切换当前字体： /C002 5 Tf
                            if (parsed[0] is PdfName)
                            {
                                currentFont = ((PdfName)parsed[0]).ToString();
                            }
                        }
                        else if (token == "Tj")
                        {
                            PdfString str = parsed[0] as PdfString;
                            if (str != null)
                            {
                                string text = DecodeString(str.GetBytes(), fontType0Map, currentFont);
                                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                            }
                        }
                        else if (token == "TJ")
                        {
                            PdfArray arr = parsed[0] as PdfArray;
                            if (arr != null)
                            {
                                StringBuilder sb = new StringBuilder();
                                foreach (PdfObject item in arr.ArrayList)
                                {
                                    PdfString str = item as PdfString;
                                    if (str != null)
                                    {
                                        sb.Append(DecodeString(str.GetBytes(), fontType0Map, currentFont));
                                    }
                                    else
                                    {
                                        sb.Append(" ");
                                    }
                                }
                                if (sb.Length > 0 && !string.IsNullOrWhiteSpace(sb.ToString()))
                                {
                                    result.Add(sb.ToString());
                                }
                            }
                        }
                        else if (token == "'")
                        {
                            PdfString str = parsed[0] as PdfString;
                            if (str != null)
                            {
                                string text = DecodeString(str.GetBytes(), fontType0Map, currentFont);
                                if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                            }
                        }
                        else if (token == "\"")
                        {
                            // " 操作符：aw ac string，字符串在第 3 项（索引 2）
                            if (parsed.Count > 2)
                            {
                                PdfString str = parsed[2] as PdfString;
                                if (str != null)
                                {
                                    string text = DecodeString(str.GetBytes(), fontType0Map, currentFont);
                                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 提取失败整体视为无文本，由调用方记录解析失败
                return new List<string>();
            }
            finally
            {
                if (reader != null)
                {
                    try { reader.Close(); } catch { /* 关闭失败忽略 */ }
                }
            }
            return result;
        }

        /// <summary>
        /// 构建当前页字体字典：字体名 -> 是否 Type0 字体。
        /// Type0（如 STSong-Light）按 UTF-16BE 解码，其余按 CP1252 解码。
        /// </summary>
        /// <param name="reader">PdfReader 实例</param>
        /// <param name="page">页码（从 1 开始）</param>
        /// <returns>字体名到是否 Type0 的映射</returns>
        private static Dictionary<string, bool> BuildFontType0Map(PdfReader reader, int page)
        {
            Dictionary<string, bool> map = new Dictionary<string, bool>();
            PdfDictionary resources = GetPageResources(reader, page);
            if (resources == null) return map;

            PdfDictionary fonts = resources.GetAsDict(PdfName.FONT);
            if (fonts == null) return map;

            foreach (PdfName key in fonts.Keys)
            {
                PdfDictionary fontDict = fonts.GetAsDict(key);
                if (fontDict == null) continue;
                PdfName subtype = fontDict.GetAsName(PdfName.SUBTYPE);
                bool isType0 = subtype != null && PdfName.TYPE0.Equals(subtype);
                map[key.ToString()] = isType0;
            }
            return map;
        }

        /// <summary>
        /// 获取页面资源字典，支持沿 Parent 继承链向上查找（部分 PDF 资源定义在页面树父节点）。
        /// </summary>
        /// <param name="reader">PdfReader 实例</param>
        /// <param name="page">页码（从 1 开始）</param>
        /// <returns>资源字典；未找到返回 null</returns>
        private static PdfDictionary GetPageResources(PdfReader reader, int page)
        {
            PdfDictionary pageDict = reader.GetPageN(page);
            PdfDictionary resources = pageDict.GetAsDict(PdfName.RESOURCES);
            if (resources != null) return resources;

            PdfDictionary parent = pageDict.GetAsDict(PdfName.PARENT);
            HashSet<string> visited = new HashSet<string>();
            while (parent != null)
            {
                string parentKey = parent.ToString();
                if (!visited.Add(parentKey)) break;
                resources = parent.GetAsDict(PdfName.RESOURCES);
                if (resources != null) return resources;
                parent = parent.GetAsDict(PdfName.PARENT);
            }
            return null;
        }

        /// <summary>
        /// 按当前字体区分解码字符串字节，并清洗为纯可打印 ASCII（乱码/中文 CID 统一转为空格）。
        /// 清洗后中文标签不可用，但关键 ASCII 值（图号/数字/工艺值）完整保留，供正则匹配。
        /// </summary>
        /// <param name="bytes">字符串原始字节</param>
        /// <param name="fontType0Map">字体名到是否 Type0 的映射</param>
        /// <param name="fontName">当前 Tf 字体名</param>
        /// <returns>清洗后的文本</returns>
        private static string DecodeString(byte[] bytes, Dictionary<string, bool> fontType0Map, string fontName)
        {
            if (bytes == null || bytes.Length == 0) return "";

            bool isType0 = false;
            if (!string.IsNullOrEmpty(fontName) && fontType0Map.ContainsKey(fontName))
            {
                isType0 = fontType0Map[fontName];
            }

            string decoded;
            if (isType0)
            {
                decoded = Encoding.BigEndianUnicode.GetString(bytes);
            }
            else
            {
                decoded = Encoding.GetEncoding(1252).GetString(bytes);
            }

            // 仅保留可打印 ASCII（空格保留），其余字符统一转为空格
            StringBuilder sb = new StringBuilder(decoded.Length);
            foreach (char c in decoded)
            {
                if (c == ' ' || (c >= 0x20 && c <= 0x7E))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }
    }
}
