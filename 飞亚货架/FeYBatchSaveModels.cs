/// 模型类（UnitInfo）
using System.Collections.Generic;

namespace kingdee.CustLI.Business.PlugInWebApi
{
    public partial class FeYBatchSave
    {
        private class UnitInfo
        {
            public string BaseUnitNumber { get; set; }
            public string StoreUnitNumber { get; set; }
            public string SaleUnitNumber { get; set; }
        }
    }
}
