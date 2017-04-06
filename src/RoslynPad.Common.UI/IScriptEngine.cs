using System.Threading;
using System.Threading.Tasks;

namespace RoslynPad.UI
{
    public interface IScriptEngine
    {
        Task<object> Execute(string code, bool echo = true, CancellationTokenSource cancellationTokenSource = null);
    }
}