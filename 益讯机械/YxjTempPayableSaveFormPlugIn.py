import clr
clr.AddReference('System')
clr.AddReference('System.Data')
clr.AddReference('Kingdee.BOS')
clr.AddReference('Kingdee.BOS.DataEntity')
clr.AddReference('Kingdee.BOS.Core')
clr.AddReference('Kingdee.BOS.ServiceHelper')

from Kingdee.BOS import *
from Kingdee.BOS.Core import *
from Kingdee.BOS.Orm.DataEntity import *
from System import *
from System.Data import *
from Kingdee.BOS.ServiceHelper import DBServiceHelper


def OnPreparePropertys(e):
	e.FieldKeys.Add("FSETACCOUNTTYPE")
	e.FieldKeys.Add("FENDDATE_H")
	e.FieldKeys.Add("AP_PAYABLEPLAN")
	e.FieldKeys.Add("AP_PAYABLEENTRY")
	e.FieldKeys.Add("FALLAMOUNTFOR_D")
	e.FieldKeys.Add("FPAYCONDITION")
	e.FieldKeys.Add("FORDERNUMBER")
	e.FieldKeys.Add("FPURCHASEORDERNO")


def BeforeExecuteOperationTransaction(e):
	for data in e.SelectedRows:
		bill = data.DataEntity

		acctType = str(bill["FSETACCOUNTTYPE"]) if bill["FSETACCOUNTTYPE"] is not None else ""
		if acctType != "2":
			continue

		payPlan = bill["AP_PAYABLEPLAN"]
		if payPlan is None or payPlan.Count > 0:
			continue

		entryObjs = bill["AP_PAYABLEENTRY"]
		if entryObjs is None or entryObjs.Count == 0:
			continue

		totalAmountFor = Decimal(0)
		for entry in entryObjs:
			amount = Convert.ToDecimal(entry["FALLAMOUNTFOR_D"] or Decimal(0))
			totalAmountFor = Decimal.Add(totalAmountFor, amount)

		firstEntry = entryObjs[0]

		newPlan = DynamicObject(payPlan.DynamicCollectionItemPropertyType)
		newPlan["ENDDATE"] = bill["FENDDATE_H"]
		newPlan["PAYAMOUNTFOR"] = Math.Round(totalAmountFor, 6)
		newPlan["FPAYRATE"] = Decimal(100)
		newPlan["PAYAMOUNT"] = Math.Round(totalAmountFor, 6)

		payConditionObj = bill["PayConditon"]
		if payConditionObj is not None:
			payConditionId = Convert.ToInt64(payConditionObj["Id"])
			sql = "SELECT FPAYMENTMETHOD FROM T_BD_PaymentCondition WHERE FID = {}".format(payConditionId)
			ds = DBServiceHelper.ExecuteDataSet(this.Context, sql)
			if ds is not None and ds.Tables.Count > 0 and ds.Tables[0].Rows.Count > 0:
				paymentMethod = Convert.ToInt32(ds.Tables[0].Rows[0]["FPAYMENTMETHOD"])
				if paymentMethod == 2:
					newPlan["FPURCHASEORDERNO"] = firstEntry["FORDERNUMBER"]

		payPlan.Add(newPlan)
