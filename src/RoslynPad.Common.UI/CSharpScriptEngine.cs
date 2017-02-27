using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace RoslynPad.UI
{
    public class CSharpScriptEngine
    {
        private static ScriptState<object> scriptState;

        private static readonly string[] Imports =
        {
            "using System;",
            "using System.Threading;",
            "using Infusion.Proxy;",
            "using Infusion.Packets;",
            "using Infusion.Proxy.InjectionApi;",
            "using Infusion.Packets.Parsers;",
            "using Infusion.Gumps;",
            "using static Infusion.Proxy.InjectionApi.Injection;"
        };

        private readonly IScriptOutput scriptOutput;

        private ScriptOptions scriptOptions;
        private string scriptRootPath;

        public string ScriptRootPath
        {
            get { return scriptRootPath; }
            set
            {
                scriptRootPath = value;
                scriptOptions =
                    scriptOptions.WithSourceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, value));
            }
        }

        public CSharpScriptEngine(IScriptOutput scriptOutput)
        {
            this.scriptOutput = scriptOutput;
            scriptOptions = ScriptOptions.Default
                .WithReferences(
                Assembly.Load("Infusion"),
                Assembly.Load("Infusion.Proxy"));
        }

        public async Task AddDefaultImports()
        {
            scriptOutput.Info("Initializing C# scripting...");
            string importsScript = string.Join("\n", Imports);
            await Execute(importsScript);
        }

        public async Task ExecuteScript(string scriptPath)
        {
            scriptOutput.Info($"Loading script: {scriptPath}");
            string scriptText = File.ReadAllText(scriptPath);

            string scriptDirectory = Path.GetDirectoryName(scriptPath);
            scriptOptions = scriptOptions.WithSourceResolver(
                ScriptSourceResolver.Default.WithSearchPaths(scriptDirectory));

            await Execute(scriptText, false);
        }

        public async Task<object> Execute(string code, bool echo = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (echo)
               scriptOutput.Echo(code);

            try
            {
                scriptState = scriptState == null
                    ? await CSharpScript.RunAsync(code, scriptOptions, cancellationToken: cancellationToken)
                    : await scriptState.ContinueWithAsync(code, scriptOptions, cancellationToken);

                var resultText = scriptState?.ReturnValue?.ToString();
                if (!string.IsNullOrEmpty(resultText))
                {
                    scriptOutput.Result(resultText);
                    return scriptState.ReturnValue;
                }

                scriptOutput.Info("OK");
            }
            catch (AggregateException ex)
            {
                scriptOutput.Error(ex.InnerExceptions
                    .Select(inner => inner.Message)
                    .Aggregate((l, r) => l + Environment.NewLine + r));
            }
            catch (Exception ex)
            {
                scriptOutput.Error(ex.Message);
            }

            return null;
        }
    }
}