import clr
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('Kingdee.BOS')
clr.AddReference('Kingdee.BOS.Core')
clr.AddReference('Kingdee.BOS.App')
clr.AddReference('Kingdee.BOS.DataEntity')
clr.AddReference('Kingdee.BOS.Contracts')
clr.AddReference('Kingdee.BOS.ServiceHelper')
from Kingdee.BOS import *
from Kingdee.BOS.Core import *
from Kingdee.BOS.Orm.DataEntity import *
from Kingdee.BOS.ServiceHelper import *
from System import *
from System.Text import StringBuilder

# 益讯机械-暂估应付单保存重算插件
# 注册位置：AP_Payable 的 <OperationServicePlugins>，OperationName = "Save"
# 触发时机：暂估应付单（立账类型 = 2）保存时
# 功能：
#   1. 付款计划重新赋值：存在付款计划则循环重新赋值（不清空、不新增、不删除）；
#      不存在则新增1行（保留原C#逻辑）。
#   2. 表头财务信息（子单据头 AP_PAYABLEFIN）6个金额字段按明细重新计算：
#      价税合计(原币) = Σ明细价税合计
#      税额(原币)     = Σ明细税额
#      不含税金额(原币) = 价税合计 - 税额
#      本位币三字段 = 原币同值（本位币=原币）
AcctTypeTemp = "2"

def OnPreparePropertys(e):
    e.FieldKeys.Add("FSETACCOUNTTYPE")
    e.FieldKeys.Add("FENDDATE_H")
    e.FieldKeys.Add("AP_PAYABLEPLAN")
    e.FieldKeys.Add("AP_PAYABLEPLAN.PAYAMOUNTFOR")
    e.FieldKeys.Add("AP_PAYABLEPLAN.PAYAMOUNT")
    e.FieldKeys.Add("AP_PAYABLEPLAN.FPAYRATE")
    e.FieldKeys.Add("AP_PAYABLEPLAN.ENDDATE")
    e.FieldKeys.Add("AP_PAYABLEPLAN.FPURCHASEORDERNO")
    e.FieldKeys.Add("AP_PAYABLEENTRY")
    e.FieldKeys.Add("AP_PAYABLEFIN")
    e.FieldKeys.Add("FALLAMOUNTFOR")
    e.FieldKeys.Add("AP_PAYABLEFIN.TaxAmountFor")
    e.FieldKeys.Add("AP_PAYABLEFIN.NoTaxAmountFor")
    e.FieldKeys.Add("AP_PAYABLEFIN.FALLAMOUNT")
    e.FieldKeys.Add("AP_PAYABLEFIN.TaxAmount")
    e.FieldKeys.Add("AP_PAYABLEFIN.NoTaxAmount")
    e.FieldKeys.Add("AP_PAYABLEENTRY.TaxPrice")
    e.FieldKeys.Add("AP_PAYABLEENTRY.FPrice")
    e.FieldKeys.Add("PayConditon")
    e.FieldKeys.Add("FORDERNUMBER")


def BeginOperationTransaction(e):
    for bill in e.DataEntitys:
        acctType = str(bill["FSETACCOUNTTYPE"]) if bill["FSETACCOUNTTYPE"] else ""
        if acctType != AcctTypeTemp:
            continue

        entryObjs = bill["AP_PAYABLEENTRY"]

        totalAll = 0
        totalTax = 0
        for entry in entryObjs:
            totalAll += Convert.ToDecimal(entry["FALLAMOUNTFOR_D"] or 0)
            totalTax += Convert.ToDecimal(entry["FTAXAMOUNTFOR_D"] or 0)
        noTax = totalAll - totalTax

        # === 0. 先执行SQL：按数据包现有明细更新分录与表头物理表 ===
        billId = Convert.ToInt64(bill["Id"])
        sb = StringBuilder()

        entryIdList = ""
        entryCaseTaxPrice = ""
        entryCasePrice = ""
        entryCaseAll = ""
        entryCaseNoTax = ""
        entryCaseTax = ""
        entryCaseAllAmt = ""
        entryCaseTaxAmt = ""
        entryCaseNoTaxAmt = ""

        for entry in entryObjs:
            eId = Convert.ToInt64(entry["Id"])
            eAll = Convert.ToDecimal(entry["FALLAMOUNTFOR_D"] or 0)
            eTax = Convert.ToDecimal(entry["FTAXAMOUNTFOR_D"] or 0)
            eNoTax = eAll - eTax
            eTaxPrice = Convert.ToDecimal(entry["TaxPrice"] or 0)
            ePrice = Convert.ToDecimal(entry["FPrice"] or 0)

            entryIdList += ("," if entryIdList != "" else "") + str(eId)
            entryCaseTaxPrice += "WHEN " + str(eId) + " THEN " + str(eTaxPrice) + " "
            entryCasePrice += "WHEN " + str(eId) + " THEN " + str(ePrice) + " "
            entryCaseAll += "WHEN " + str(eId) + " THEN " + str(eAll) + " "
            entryCaseNoTax += "WHEN " + str(eId) + " THEN " + str(eNoTax) + " "
            entryCaseTax += "WHEN " + str(eId) + " THEN " + str(eTax) + " "
            entryCaseAllAmt += "WHEN " + str(eId) + " THEN " + str(eAll) + " "
            entryCaseTaxAmt += "WHEN " + str(eId) + " THEN " + str(eTax) + " "
            entryCaseNoTaxAmt += "WHEN " + str(eId) + " THEN " + str(eNoTax) + " "

        if entryIdList != "":
            sb.Append("UPDATE T_AP_PAYABLEENTRY SET ")
            sb.Append("FTAXPRICE = CASE FENTRYID " + entryCaseTaxPrice + "END, ")
            sb.Append("FPrice = CASE FENTRYID " + entryCasePrice + "END, ")
            sb.Append("FALLAMOUNTFOR = CASE FENTRYID " + entryCaseAll + "END, ")
            sb.Append("FNoTaxAmountFor = CASE FENTRYID " + entryCaseNoTax + "END, ")
            sb.Append("FTAXAMOUNTFOR = CASE FENTRYID " + entryCaseTax + "END, ")
            sb.Append("FALLAMOUNT = CASE FENTRYID " + entryCaseAllAmt + "END, ")
            sb.Append("FTAXAMOUNT = CASE FENTRYID " + entryCaseTaxAmt + "END, ")
            sb.Append("FNOTAXAMOUNT = CASE FENTRYID " + entryCaseNoTaxAmt + "END ")
            sb.Append("WHERE FENTRYID IN (" + entryIdList + ") AND FID = " + str(billId) + ";")

            sb.Append("UPDATE T_AP_PAYABLE SET ")
            sb.Append("FALLAMOUNTFOR = " + str(totalAll) + " ")
            sb.Append("WHERE FID = " + str(billId) + ";")

            sb.Append("UPDATE T_AP_PAYABLEFIN SET ")
            sb.Append("FALLAMOUNT = " + str(totalAll) + ", ")
            sb.Append("FTAXAMOUNT = " + str(totalTax) + ", ")
            sb.Append("FNOTAXAMOUNT = " + str(noTax) + ", ")
            sb.Append("TaxAmountFor = " + str(totalTax) + ", ")
            sb.Append("NoTaxAmountFor = " + str(noTax) + " ")
            sb.Append("WHERE FID = " + str(billId) + ";")

            try:
                DBServiceHelper.ExecuteDataSet(this.Context, sb.ToString())
            except Exception as ex:
                pass

        # 价税合计(原币) 写在单据头 AP_PAYABLE
        bill["FALLAMOUNTFOR"] = totalAll

        # 税额/不含税(原币) + 本位币三字段 写在财务子单据头 AP_PAYABLEFIN
        fin = bill["AP_PAYABLEFIN"]
        if fin is not None and fin.Count > 0:
            f0 = fin[0]
            f0["TaxAmountFor"]   = totalTax   # 税额(原币)
            f0["NoTaxAmountFor"] = noTax      # 不含税金额(原币) = 价税合计 - 税额
            f0["FALLAMOUNT"]     = totalAll   # 价税合计(本位币)
            f0["TaxAmount"]      = totalTax   # 税额(本位币)
            f0["NoTaxAmount"]    = noTax      # 不含税金额(本位币)

        # === 2. 付款计划重算 ===
        payPlan = bill["AP_PAYABLEPLAN"]
        if payPlan is not None and payPlan.Count > 0:
            # 存在则循环重新赋值，不新增、不删除、不清空
            for plan in payPlan:
                plan["PAYAMOUNTFOR"] = Math.Round(totalAll, 6)
                plan["PAYAMOUNT"]    = Math.Round(totalAll, 6)
                plan["FPAYRATE"]     = 100
                plan["ENDDATE"]      = bill["FENDDATE_H"]
            # 付款条件 = 2 时给首行赋采购订单号（保留原逻辑）
            _fillPurchaseOrderNo(bill, payPlan, entryObjs)
        else:
            # 不存在则新增1行（保留原C#逻辑）
            newPlan = DynamicObject(payPlan.DynamicCollectionItemPropertyType)
            newPlan["PAYAMOUNTFOR"] = Math.Round(totalAll, 6)
            newPlan["FPAYRATE"]     = 100
            newPlan["PAYAMOUNT"]    = Math.Round(totalAll, 6)
            _fillPurchaseOrderNo_New(bill, newPlan, entryObjs)
            payPlan.Add(newPlan)

        raise Exception("YxjDebug SQL完成 totalAll=" + str(totalAll)
            + " | 付款计划 Count=" + str(payPlan.Count if payPlan is not None else -1))

def _fillPurchaseOrderNo(bill, payPlan, entryObjs):
    payCondObj = bill["PayConditon"]
    if payCondObj is None:
        return
    payCondId = Convert.ToInt64(payCondObj["Id"])
    sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = " + str(payCondId)
    ds = DBServiceHelper.ExecuteDataSet(this.Context, sql)
    if ds is not None and ds.Tables.Count > 0 and ds.Tables[0].Rows.Count > 0:
        method = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"])
        if method == 2:
            first = entryObjs[0]
            payPlan[0]["FPURCHASEORDERNO"] = first["FORDERNUMBER"]


def _fillPurchaseOrderNo_New(bill, newPlan, entryObjs):
    payCondObj = bill["PayConditon"]
    if payCondObj is None:
        return
    payCondId = Convert.ToInt64(payCondObj["Id"])
    sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = " + str(payCondId)
    ds = DBServiceHelper.ExecuteDataSet(this.Context, sql)
    if ds is not None and ds.Tables.Count > 0 and ds.Tables[0].Rows.Count > 0:
        method = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"])
        if method == 2:
            first = entryObjs[0]
            newPlan["FPURCHASEORDERNO"] = first["FORDERNUMBER"]

