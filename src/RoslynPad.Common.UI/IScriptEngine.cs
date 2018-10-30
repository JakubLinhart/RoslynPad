using System.Threading;
using System.Threading.Tasks;

namespace RoslynPad.UI
{
    public interface IScriptEngine
    {
        Task<object> Execute(string code, string filePath, bool wholeFile, CancellationTokenSource cancellationTokenSource = null);
    }
}