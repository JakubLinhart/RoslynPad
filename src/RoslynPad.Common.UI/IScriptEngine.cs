using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynPad.UI
{
    public interface IScriptEngine
    {
        Task<object> Execute(string code, bool echo = true,
            CancellationToken cancellationToken = default(CancellationToken));

    }
}
