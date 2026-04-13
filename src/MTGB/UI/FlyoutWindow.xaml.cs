using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MTGB.UI;

public partial class FlyoutWindow : System.Windows.Window
{
    public void SlideUp() { }
    public void SlideDown() { }
    public void RefreshPrinterCards(
        System.Collections.Generic.IReadOnlyDictionary
        <int, MTGB.Services.PrinterSnapshot> snapshots)
    { }
}
