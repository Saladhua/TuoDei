import clr
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('Kingdee.BOS')
clr.AddReference('Kingdee.BOS.DataEntity')
clr.AddReference('Kingdee.BOS.Core')
clr.AddReference('Kingdee.BOS.ServiceHelper')

from Kingdee.BOS import *
from Kingdee.BOS.Core import *
from Kingdee.BOS.Core.DynamicForm import OperateResult
from Kingdee.BOS.Orm.DataEntity import *
from System import *
from System.Data import *
from Kingdee.BOS.ServiceHelper import DBServiceHelper


def OnPreparePropertys(e):
	e.FieldKeys.Add("FSETACCOUNTTYPE")
	e.FieldKeys.Add("AP_PAYABLEPLAN")
	e.FieldKeys.Add("AP_PAYABLEENTRY")


def AfterExecuteOperationTransaction(e):
	for entity in e.DataEntitys:
		acctType = str(entity["FSETACCOUNTTYPE"]) if entity["FSETACCOUNTTYPE"] is not None else ""
		if acctType != "2":
			continue

		payPlan = entity["AP_PAYABLEPLAN"]
		if payPlan is None or payPlan.Count == 0:
			continue

		entries = entity["AP_PAYABLEENTRY"]
		if entries is None or entries.Count == 0:
			continue

		billId = Convert.ToInt64(entity["Id"])
		firstEntryId = Convert.ToInt64(entries[0]["Id"])

		sql = ("/*dialect*/ UPDATE T_AP_PAYABLEPLAN "
			"SET FENTRYID={0},FSEQ='1' "
			"WHERE FID={1} AND (FENTRYID IS NULL OR FENTRYID=0)").format(firstEntryId, billId)
		DBServiceHelper.ExecuteDataSet(this.Context, sql)

		result = OperateResult()
		result.SuccessStatus = True
		result.Message = "付款计划行号更新成功"
		this.OperationResult.OperateResult.Add(result)
