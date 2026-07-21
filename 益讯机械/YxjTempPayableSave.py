import clr
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('Kingdee.BOS')
clr.AddReference('Kingdee.BOS.DataEntity')
clr.AddReference('Kingdee.BOS.Core')
clr.AddReference('Kingdee.BOS.App')
clr.AddReference('Kingdee.BOS.ServiceHelper')

from Kingdee.BOS import *
from Kingdee.BOS.Core import *
from Kingdee.BOS.Core.Bill import *
from Kingdee.BOS.Core.DynamicForm.PlugIn import *
from Kingdee.BOS.Core.DynamicForm.PlugIn.Args import *
from System import *
from System.Data import *
from Kingdee.BOS.ServiceHelper import *
from Kingdee.BOS.Orm.DataEntity import *


def AfterCreateModelData(e):
    _GeneratePaymentPlan()


def DataChanged(e):
    if e.Field is not None and e.Field.Key == "FSETACCOUNTTYPE":
        _GeneratePaymentPlan()


def BeforeDoOperation(e):
    opCode = e.Operation.FormOperation.Operation.ToUpperInvariant()
    if opCode == "SAVE":
        _GeneratePaymentPlan()


def _GeneratePaymentPlan():
    billObj = this.View.Model.DataObject

    acctType = str(billObj["FSETACCOUNTTYPE"]) if billObj["FSETACCOUNTTYPE"] is not None else ""
    if acctType != "2":
        return

    payPlan = billObj["AP_PAYABLEPLAN"]
    if payPlan is None or payPlan.Count > 0:
        return

    entryObjs = billObj["AP_PAYABLEENTRY"]
    if entryObjs is None or entryObjs.Count == 0:
        return

    totalAmountFor = Decimal(0)
    for entry in entryObjs:
        val = entry["FALLAMOUNTFOR_D"]
        if val is not None:
            totalAmountFor = Decimal.Add(totalAmountFor, Convert.ToDecimal(val))

    firstEntry = entryObjs[0]

    newPlan = DynamicObject(payPlan.DynamicCollectionItemPropertyType)
    newPlan["ENDDATE"] = billObj["FENDDATE_H"]
    newPlan["PAYAMOUNTFOR"] = Decimal.Round(totalAmountFor, 6)
    newPlan["FPAYRATE"] = Decimal(100)
    newPlan["PAYAMOUNT"] = Decimal.Round(totalAmountFor, 6)
    newPlan["FWRITTENOFFSTATUS"] = "A"
    newPlan["FNOTVERIFICATEAMOUNT"] = Decimal.Round(totalAmountFor, 6)
    newPlan["FENTRYID"] = firstEntry["FENTRYID"]
    newPlan["FSEQ"] = 1

    payConditionObj = billObj["PayConditon"]
    if payConditionObj is not None:
        payConditionId = payConditionObj["Id"]
        sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {0}".format(payConditionId)
        ds = DBServiceHelper.ExecuteDataSet(this.View.Context, sql)
        if ds is not None and ds.Tables.Count > 0 and ds.Tables[0].Rows.Count > 0:
            paymentMethod = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"])
            if paymentMethod == 2:
                newPlan["FPURCHASEORDERNO"] = firstEntry["FORDERNUMBER"]

    payPlan.Add(newPlan)
