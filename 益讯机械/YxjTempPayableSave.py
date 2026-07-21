import clr
# 引用必备的程序集
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('Kingdee.BOS')
clr.AddReference('Kingdee.BOS.DataEntity')
clr.AddReference('Kingdee.BOS.Core')
clr.AddReference('Kingdee.BOS.App')
clr.AddReference('Kingdee.BOS.ServiceHelper')

# 导入所需命名空间
from Kingdee.BOS import *
from Kingdee.BOS.Core import *
from Kingdee.BOS.Core.Bill import *
from Kingdee.BOS.Core.DynamicForm.PlugIn import *
from Kingdee.BOS.Core.DynamicForm.PlugIn.Args import *
from System import *
from System.Data import *
from Kingdee.BOS.ServiceHelper import *
from Kingdee.BOS.Orm.DataEntity import *


# ==============================
# 事件1：新增单据初始化后触发
# ==============================
def AfterCreateModelData(e):
    """
    新增暂估应付单时，若核算类型为暂估(2)，自动生成付款计划。
    新开界面用户尚未填写数据可能为空，但框架加载默认值后此事件触发，
    此时FSETACCOUNTTYPE可能尚未选择，因此内部逻辑会判断为空则跳过。
    """
    _生成付款计划()


# ==============================
# 事件2：字段值改变时触发
# ==============================
def DataChanged(e):
    """
    监听核算类型字段 FSETACCOUNTTYPE 的切换事件。
    当用户将核算类型切换为暂估(2)时，立即生成付款计划。
    """
    if e.Field is not None and e.Field.Key == "FSETACCOUNTTYPE":
        _生成付款计划()


# ==============================
# 事件3：执行操作前触发（最终兜底）
# ==============================
def BeforeDoOperation(e):
    """
    在保存操作执行前最后检查一次。
    如果前面两个事件都没生成付款计划（例如用户在最后才切换核算类型为暂估），
    这里作为最终的兜底保障。
    """
    # 只拦截保存操作
    opCode = e.Operation.FormOperation.Operation.ToUpperInvariant()
    if opCode == "SAVE":
        _生成付款计划()


# ==============================
# 核心逻辑：生成付款计划
# ==============================
def _生成付款计划():
    """
    根据暂估应付单的物料分录总金额，自动生成一条付款计划。

    关键前提:
    - 核算类型(FSETACCOUNTTYPE)必须为"2"(暂估)
    - 付款计划子单据体(AP_PAYABLEPLAN)当前必须为空（已有则不重复生成）
    - 物料分录子单据体(AP_PAYABLEENTRY)必须至少有一条分录

    付款计划各字段赋值说明:
    - ENDDATE          → 单据头结算日期(FENDDATE_H)
    - PAYAMOUNTFOR     → 物料分录价税合计之和，保留6位小数
    - FPAYRATE         → 固定100(百分比)
    - PAYAMOUNT        → 同PAYAMOUNTFOR
    - FWRITTENOFFSTATUS → "A"(未核销)
    - FNOTVERIFICATEAMOUNT → 同PAYAMOUNTFOR(未核销金额)
    - FENTRYID         → 取第一条物料分录的FENTRYID
    - FSEQ             → 固定1
    - FPURCHASEORDERNO → 仅当结算方式=2(订单结算)时，取第一条物料分录的订单号(FORDERNUMBER)
    """
    # 获取当前单据数据对象
    单据 = this.View.Model.DataObject

    # 判断核算类型是否为暂估(2)，非暂估不生成付款计划
    核算类型 = str(单据["FSETACCOUNTTYPE"]) if 单据["FSETACCOUNTTYPE"] is not None else ""
    if 核算类型 != "2":
        return

    # 如果付款计划已经存在，不重复生成
    付款计划集合 = 单据["AP_PAYABLEPLAN"]
    if 付款计划集合 is None or 付款计划集合.Count > 0:
        return

    # 获取物料分录集合，为空则不处理
    物料分录集合 = 单据["AP_PAYABLEENTRY"]
    if 物料分录集合 is None or 物料分录集合.Count == 0:
        return

    # 累计所有物料分录的价税合计金额
    价税合计 = Decimal(0)
    for 分录 in 物料分录集合:
        金额 = 分录["FALLAMOUNTFOR_D"]
        if 金额 is not None:
            价税合计 = Decimal.Add(价税合计, Convert.ToDecimal(金额))

    # 取第一条物料分录，后续生成付款计划时需要引用其部分字段
    第一条分录 = 物料分录集合[0]

    # 创建付款计划动态对象
    付款计划 = DynamicObject(付款计划集合.DynamicCollectionItemPropertyType)

    # ----- 赋值付款计划各字段 -----
    付款计划["ENDDATE"] = 单据["FENDDATE_H"]                         # 结算日期
    付款计划["PAYAMOUNTFOR"] = Decimal.Round(价税合计, 6)            # 应付金额
    付款计划["FPAYRATE"] = Decimal(100)                              # 付款比例(100%)
    付款计划["PAYAMOUNT"] = Decimal.Round(价税合计, 6)               # 付款金额
    付款计划["FWRITTENOFFSTATUS"] = "A"                              # 核销状态：未核销
    付款计划["FNOTVERIFICATEAMOUNT"] = Decimal.Round(价税合计, 6)    # 未核销金额
    付款计划["FENTRYID"] = 第一条分录["FENTRYID"]                      # 关联第一条物料分录ID
    付款计划["FSEQ"] = 1                                              # 序号

    # ----- 处理订单号(仅结算方式为2时才有) -----
    结算方式对象 = 单据["PayConditon"]
    if 结算方式对象 is not None:
        # 查询结算方式主表获取付款方式
        结算方式ID = 结算方式对象["Id"]
        sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {0}".format(结算方式ID)
        结果 = DBServiceHelper.ExecuteDataSet(this.View.Context, sql)
        if 结果 is not None and 结果.Tables.Count > 0 and 结果.Tables[0].Rows.Count > 0:
            付款方式 = Convert.ToInt32(结果.Tables[0].Rows[0]["FPAYMENTMETHOD"])
            # 付款方式=2（订单结算）：取物料分录的原始订单号
            if 付款方式 == 2:
                付款计划["FPURCHASEORDERNO"] = 第一条分录["FORDERNUMBER"]

    # 将新生成的付款计划添加到付款计划子单据体
    付款计划集合.Add(付款计划)
