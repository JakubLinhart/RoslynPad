using System;
using System.Collections.Generic;
using System.Composition;
using System.Reflection;
using RoslynPad.UI;
using System.Collections.Immutable;

namespace RoslynPad
{
    [Export(typeof(MainViewModelBase)), Shared]
    public class MainViewModel : MainViewModelBase
    {
        [ImportingConstructor]
        public MainViewModel(IServiceProvider serviceProvider, ICommandProvider commands, IApplicationSettings settings, DocumentFileWatcher documentFileWatcher) : base(serviceProvider, commands, settings, documentFileWatcher)
        {
        }

        protected override IEnumerable<Assembly> CompositionAssemblies => ImmutableArray.Create(
            Assembly.Load(new AssemblyName("RoslynPad.Roslyn.Windows")),
            Assembly.Load(new AssemblyName("RoslynPad.Editor.Windows")));
    }
}