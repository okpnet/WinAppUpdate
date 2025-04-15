using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppUpdater
{
    public interface ICancelEventArg
    {
        bool IsCancel { get; set; }
    }
}
