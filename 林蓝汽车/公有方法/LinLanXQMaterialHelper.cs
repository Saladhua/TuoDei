using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    public static class LinLanXQMaterialHelper
    {
        public static Dictionary<string, long> BatchQueryMaterialByDrawingNos(Context ctx, List<string> drawingNoList)
        {
            Dictionary<string, long> result = new Dictionary<string, long>();

            if (drawingNoList == null || drawingNoList.Count == 0) return result;

            StringBuilder sql = new StringBuilder();
            sql.AppendLine("SELECT");
            sql.AppendLine("    a1.FMATERIALID,");
            sql.AppendLine("    a1.F_QSGA_TEXT_33Z AS FDRAWINGNO");
            sql.AppendLine("FROM T_BD_MATERIAL a1");
            sql.AppendLine("WHERE a1.F_QSGA_TEXT_33Z IN (");

            for (int i = 0; i < drawingNoList.Count; i++)
            {
                if (i > 0) sql.Append(",");
                sql.Append("'" + drawingNoList[i].Replace("'", "''") + "'");
            }

            sql.AppendLine(")");

            DataSet ds = null;
            try
            {
                ds = DBServiceHelper.ExecuteDataSet(ctx, sql.ToString());
            }
            catch
            {
                return result;
            }

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    long materialId = Convert.ToInt64(row["FMATERIALID"]);
                    string drawingNo = row["FDRAWINGNO"].ToString();
                    if (!result.ContainsKey(drawingNo))
                    {
                        result[drawingNo] = materialId;
                    }
                }
            }

            return result;
        }
    }
}
