using System;
using System.Data;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Core;
using Kingdee.BOS.Core.Validation;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;

namespace kingdee.CustLI.Business.PlugIn
{
    /// <summary>
    /// 林蓝汽车-物料清单-BOM包装方式查重校验器
    /// 校验规则：同一"父项物料 + 包装方式"不允许存在多个已审核/已创建状态的BOM版本。
    /// 冲突时在保存时阻断并提示错误信息，由 UI 展示给用户。
    /// </summary>
    [System.ComponentModel.Description("林蓝汽车-物料清单-BOM包装方式查重校验")]
    public class LinLanXQBomPackMethodValidator : AbstractValidator
    {
        /// <summary>
        /// 执行校验：遍历所有待保存的 BOM 单据，逐个检查是否存在重复版本
        /// </summary>
        /// <param name="dataEntities">待校验的单据实体数组</param>
        /// <param name="validateContext">校验上下文，用于添加错误信息</param>
        /// <param name="ctx">金蝶上下文对象</param>
        public override void Validate(ExtendedDataEntity[] dataEntities, ValidateContext validateContext, Context ctx)
        {
            if (dataEntities == null || dataEntities.Length == 0) return;

            foreach (ExtendedDataEntity entity in dataEntities)
            {
                DynamicObject billObj = entity.DataEntity;
                if (billObj == null) continue;

                // 取父项物料内码：FMATERIALID 指向 BOM 的父项物料基础资料
                long parentMaterialId = 0;
                // 取包装方式内码：F_CustLI_PackMethod 为自定义字段，记录 BOM 的包装方式
                long packMethodId = 0;

                if (billObj["FMATERIALID"] != null)
                {
                    DynamicObject matObj = billObj["FMATERIALID"] as DynamicObject;
                    if (matObj != null)
                    {
                        parentMaterialId = Convert.ToInt64(matObj["Id"]);
                    }
                }

                if (billObj["F_CustLI_PackMethod"] != null)
                {
                    DynamicObject packObj = billObj["F_CustLI_PackMethod"] as DynamicObject;
                    if (packObj != null)
                    {
                        packMethodId = Convert.ToInt64(packObj["Id"]);
                    }
                }

                // 包装方式或父项物料为空时跳过校验（空值不会产生重复冲突）
                if (packMethodId <= 0) continue;
                if (parentMaterialId <= 0) continue;

                // 查 DB 判断是否存在重复 BOM 版本
                bool exists = CheckDuplicateBom(ctx, parentMaterialId, packMethodId, entity.DataEntity["Id"]);
                if (exists)
                {
                    validateContext.AddError(entity, new ValidationErrorInfo(
                        "F_CustLI_PackMethod",
                        entity.DataEntity["Id"].ToString(),
                        0,
                        0,
                        entity.DataEntity["Id"].ToString(),
                        "父项物料编码 + 包装方式 已存在BOM版本，不允许重复创建。",
                        "",
                        ErrorLevel.Error));
                }
            }
        }

        /// <summary>
        /// 查询数据库判断同一父项物料+包装方式的BOM是否已存在
        /// 查询范围：已审核(A)和已创建(C)的BOM，编辑时排除当前BOM自身
        /// </summary>
        /// <param name="ctx">金蝶上下文对象</param>
        /// <param name="parentMaterialId">父项物料内码</param>
        /// <param name="packMethodId">包装方式内码</param>
        /// <param name="currentBomId">当前BOM的ID（编辑时用于排除自身）</param>
        /// <returns>true=已存在重复版本</returns>
        private bool CheckDuplicateBom(Context ctx, long parentMaterialId, long packMethodId, object currentBomId)
        {
            // 解析当前 BOM ID，编辑场景下需排除自身，新增场景下为 0
            long currentId = 0;
            if (currentBomId != null)
            {
                long.TryParse(currentBomId.ToString(), out currentId);
            }

            // 查询 T_ENG_BOM 主表 + T_ENG_BOM_EXT 扩展表：
            // 按父项物料 + 包装方式统计已生效的 BOM 版本数量
            StringBuilder sql = new StringBuilder();
            sql.AppendLine("SELECT COUNT(1) AS FCOUNT");
            sql.AppendLine("FROM T_ENG_BOM a1");
            sql.AppendLine("WHERE a1.FMATERIALID = " + parentMaterialId.ToString());
            // A=已审核(Approved), C=已创建(Created)，已废弃的BOM不参与查重
            sql.AppendLine("AND a1.FDOCUMENTSTATUS IN ('A', 'C')");

            // 编辑场景下排除当前 BOM 自身，避免把自己也算作重复
            if (currentId > 0)
            {
                sql.AppendLine("AND a1.FID != " + currentId.ToString());
            }

            // 用 EXISTS 子查询关联扩展表：只关心"是否存在"而非具体行数据，语义清晰且性能更好
            sql.AppendLine("AND EXISTS (");
            sql.AppendLine("    SELECT 1 FROM T_ENG_BOM_EXT b1");
            sql.AppendLine("    WHERE b1.FID = a1.FID");
            sql.AppendLine("    AND b1.F_CustLI_PackMethod = " + packMethodId.ToString());
            sql.AppendLine(")");

            try
            {
                DataSet ds = DBServiceHelper.ExecuteDataSet(ctx, sql.ToString());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int count = Convert.ToInt32(ds.Tables[0].Rows[0]["FCOUNT"]);
                    return count > 0;
                }
            }
            catch
            {
                // 查询异常时保守处理，不阻断保存，避免DB抖动导致正常单据无法提交
                return false;
            }

            return false;
        }
    }
}
