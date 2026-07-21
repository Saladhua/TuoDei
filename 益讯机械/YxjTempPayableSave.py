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
    _generatePaymentPlan()


# ==============================
# 事件2：字段值改变时触发
# ==============================
def DataChanged(e):
    """
    监听核算类型字段 FSETACCOUNTTYPE 的切换事件。
    当用户将核算类型切换为暂估(2)时，立即生成付款计划。
    """
    if e.Field is not None and e.Field.Key == "FSETACCOUNTTYPE":
        _generatePaymentPlan()


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
        _generatePaymentPlan()


# ==============================
# 核心逻辑：生成付款计划
# ==============================
def _generatePaymentPlan():
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
    - FPURCHASEORDERNO → 仅当结算方式=2(订单结算)时，取第一条物料分录的订单号(FORDERNUMBER)
    """
    # 获取当前单据数据对象
    bill = this.View.Model.DataObject

    # 判断核算类型是否为暂估(2)，非暂估不生成付款计划
    acctType = str(bill["FSETACCOUNTTYPE"]) if bill["FSETACCOUNTTYPE"] is not None else ""
    if acctType != "2":
        return

    # 如果付款计划已经存在，不重复生成
    payPlan = bill["AP_PAYABLEPLAN"]
    if payPlan is None or payPlan.Count > 0:
        return

    # 获取物料分录集合，为空则不处理
    entries = bill["AP_PAYABLEENTRY"]
    if entries is None or entries.Count == 0:
        return

    # 累计所有物料分录的价税合计金额
    totalAmountFor = Decimal(0)
    for entry in entries:
        amount = entry["FALLAMOUNTFOR_D"]
        if amount is not None:
            totalAmountFor = Decimal.Add(totalAmountFor, Convert.ToDecimal(amount))

    # 取第一条物料分录，后续生成付款计划时需要引用其部分字段
    firstEntry = entries[0]

    # 创建付款计划动态对象
    newPlan = DynamicObject(payPlan.DynamicCollectionItemPropertyType)

    # ----- 赋值付款计划各字段 -----
    newPlan["ENDDATE"] = bill["FENDDATE_H"]                         # 结算日期
    newPlan["PAYAMOUNTFOR"] = Decimal.Round(totalAmountFor, 6)        # 应付金额
    newPlan["FPAYRATE"] = Decimal(100)                                # 付款比例(100%)
    newPlan["PAYAMOUNT"] = Decimal.Round(totalAmountFor, 6)           # 付款金额

    # ----- 处理订单号(仅结算方式为2时才有) -----
    payCondition = bill["PayConditon"]
    if payCondition is not None:
        # 查询结算方式主表获取付款方式
        payConditionId = payCondition["Id"]
        sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {0}".format(payConditionId)
        ds = DBServiceHelper.ExecuteDataSet(this.View.Context, sql)
        if ds is not None and ds.Tables.Count > 0 and ds.Tables[0].Rows.Count > 0:
            paymentMethod = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"])
            # 付款方式=2（订单结算）：取物料分录的原始订单号
            if paymentMethod == 2:
                newPlan["FPURCHASEORDERNO"] = firstEntry["FORDERNUMBER"]

    # 将新生成的付款计划添加到付款计划子单据体
    payPlan.Add(newPlan)
