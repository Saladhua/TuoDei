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
from Kingdee.BOS.App.Data import DBUtils
from Kingdee.BOS.Orm.DataEntity import *


def BeforeDoOperation(e):
    opCode = e.Operation.FormOperation.Operation.ToUpperInvariant()
    if opCode != "SUBMIT":
        return

    bill = this.View.Model.DataObject

    acctType = str(bill["FSETACCOUNTTYPE"]) if bill["FSETACCOUNTTYPE"] is not None else ""
    if acctType != "2":
        return

    payPlan = bill["AP_PAYABLEPLAN"]
    if payPlan is None or payPlan.Count == 0:
        return

    entries = bill["AP_PAYABLEENTRY"]
    if entries is None or entries.Count == 0:
        return

    billId = Convert.ToInt64(bill["Id"])
    firstEntryId = Convert.ToInt64(entries[0]["Id"])

    sql = "/*dialect*/ UPDATE T_AP_PAYABLEPLAN SET FENTRYID={0},FSEQ='1' WHERE FID={1} AND (FENTRYID IS NULL OR FENTRYID=0)".format(firstEntryId, billId)
    count = DBUtils.Execute(this.Context, sql)
    this.View.ShowMessage("付款计划行号更新成功，SQL: " + sql + "\n受影响行数: " + str(count))
