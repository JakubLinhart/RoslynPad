using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Practices.ServiceLocation;
using NuGet.Versioning;
using RoslynPad.Roslyn.Diagnostics;
using RoslynPad.Roslyn.Rename;
using RoslynPad.Runtime;
using RoslynPad.Utilities;

namespace RoslynPad.UI
{
    [Export]
    public class OpenDocumentViewModel : NotificationObject
    {
        private readonly IServiceLocator _serviceLocator;
        private string _workingDirectory;
        private readonly Dispatcher _dispatcher;

        private ObservableCollection<ResultObject> _results;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private Action<object> _executionHostOnDumped;
        private bool _isDirty;
        private Platform _platform;
        private bool _isSaving;
        private IDisposable _viewDisposable;
        private Action<ExceptionResultObject> _onError;
        private Func<TextSpan> _getSelection;

        public DocumentViewModel Document { get; private set; }

        [ImportingConstructor]
        public OpenDocumentViewModel(IServiceLocator serviceLocator, MainViewModel mainViewModel, ICommandProvider commands)
        {
            _serviceLocator = serviceLocator;
            MainViewModel = mainViewModel;
            CommandProvider = commands;
            _dispatcher = Dispatcher.CurrentDispatcher;

            SaveCommand = commands.CreateAsync(() => Save(promptSave: false));
            RunCommand = commands.CreateAsync(Run, () => !IsRunning);
            FormatDocumentCommand = commands.CreateAsync(FormatDocument);
            CommentSelectionCommand = commands.CreateAsync(() => CommentUncommentSelection(CommentAction.Comment));
            UncommentSelectionCommand = commands.CreateAsync(() => CommentUncommentSelection(CommentAction.Uncomment));
            RenameSymbolCommand = commands.CreateAsync(RenameSymbol);
        }

        public void SetDocument(DocumentViewModel document)
        {
            Document = document;

            IsDirty = document?.IsAutoSave == true;

            _workingDirectory = Document != null
                ? Path.GetDirectoryName(Document.Path)
                : MainViewModel.DocumentRoot.Path;
        }

        private async Task RenameSymbol()
        {
            var host = MainViewModel.RoslynHost;
            var document = host.GetDocument(DocumentId);
            var symbol = await RenameHelper.GetRenameSymbol(document, _getSelection().Start).ConfigureAwait(true);
            if (symbol == null) return;

            var dialog = _serviceLocator.GetInstance<IRenameSymbolDialog>();
            dialog.Initialize(symbol.Name);
            dialog.Show();
            if (dialog.ShouldRename)
            {
                var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, symbol, dialog.SymbolName, null).ConfigureAwait(true);
                var newDocument = newSolution.GetDocument(DocumentId);
                // TODO: possibly update entire solution
                host.UpdateDocument(newDocument);
            }
            OnEditorFocus();
        }

        private enum CommentAction
        {
            Comment,
            Uncomment
        }

        private async Task CommentUncommentSelection(CommentAction action)
        {
            const string singleLineCommentString = "//";
            var document = MainViewModel.RoslynHost.GetDocument(DocumentId);
            var selection = _getSelection();
            var documentText = await document.GetTextAsync().ConfigureAwait(false);
            var changes = new List<TextChange>();
            var lines = documentText.Lines.SkipWhile(x => !x.Span.IntersectsWith(selection))
                .TakeWhile(x => x.Span.IntersectsWith(selection)).ToArray();

            if (action == CommentAction.Comment)
            {
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(documentText.GetSubText(line.Span).ToString()))
                    {
                        changes.Add(new TextChange(new TextSpan(line.Start, 0), singleLineCommentString));
                    }
                }
            }
            else if (action == CommentAction.Uncomment)
            {
                foreach (var line in lines)
                {
                    var text = documentText.GetSubText(line.Span).ToString();
                    if (text.TrimStart().StartsWith(singleLineCommentString, StringComparison.Ordinal))
                    {
                        changes.Add(new TextChange(new TextSpan(
                            line.Start + text.IndexOf(singleLineCommentString, StringComparison.Ordinal),
                            singleLineCommentString.Length), string.Empty));
                    }
                }
            }

            if (changes.Count == 0) return;

            MainViewModel.RoslynHost.UpdateDocument(document.WithText(documentText.WithChanges(changes)));
            if (action == CommentAction.Uncomment)
            {
                await FormatDocument().ConfigureAwait(false);
            }
        }

        private async Task FormatDocument()
        {
            var document = MainViewModel.RoslynHost.GetDocument(DocumentId);
            var formattedDocument = await Formatter.FormatAsync(document).ConfigureAwait(false);
            MainViewModel.RoslynHost.UpdateDocument(formattedDocument);
        }

        private void SetIsRunning(bool value)
        {
            _dispatcher.InvokeAsync(() => IsRunning = value);
        }

        public async Task AutoSave()
        {
            if (!IsDirty) return;
            if (Document == null)
            {
                var index = 1;
                string path;
                do
                {
                    path = Path.Combine(_workingDirectory, DocumentViewModel.GetAutoSaveName("Program" + index++));
                } while (File.Exists(path));
                Document = DocumentViewModel.CreateAutoSave(path);
            }

            await SaveDocument(Document.GetAutoSavePath()).ConfigureAwait(false);
        }

        public async Task<SaveResult> Save(bool promptSave)
        {
            if (_isSaving) return SaveResult.Cancel;
            if (!IsDirty && promptSave) return SaveResult.Save;

            _isSaving = true;
            try
            {
                var result = SaveResult.Save;
                if (Document == null || Document.IsAutoSaveOnly)
                {
                    var dialog = _serviceLocator.GetInstance<ISaveDocumentDialog>();
                    dialog.ShowDontSave = promptSave;
                    dialog.AllowNameEdit = true;
                    dialog.FilePathFactory = s => DocumentViewModel.GetDocumentPathFromName(_workingDirectory, s);
                    dialog.Show();
                    result = dialog.Result;
                    if (result == SaveResult.Save)
                    {
                        if (Document?.IsAutoSave == true)
                        {
                            File.Delete(Document.Path);
                        }
                        Document = MainViewModel.AddDocument(dialog.DocumentName);
                        OnPropertyChanged(nameof(Title));
                    }
                }
                else if (promptSave)
                {
                    var dialog = _serviceLocator.GetInstance<ISaveDocumentDialog>();
                    dialog.ShowDontSave = true;
                    dialog.DocumentName = Document.Name;
                    dialog.Show();
                    result = dialog.Result;
                }
                if (result == SaveResult.Save)
                {
                    // ReSharper disable once PossibleNullReferenceException
                    await SaveDocument(Document.GetSavePath()).ConfigureAwait(true);
                    IsDirty = false;
                }
                return result;
            }
            finally
            {
                _isSaving = false;
            }
        }

        private async Task SaveDocument(string path)
        {
            if (DocumentId == null) return;

            var text = await MainViewModel.RoslynHost.GetDocument(DocumentId).GetTextAsync().ConfigureAwait(false);
            using (var writer = new StreamWriter(path, append: false))
            {
                for (int lineIndex = 0; lineIndex < text.Lines.Count - 1; ++lineIndex)
                {
                    var lineText = text.Lines[lineIndex].ToString();
                    await writer.WriteLineAsync(lineText).ConfigureAwait(false);
                }
                await writer.WriteAsync(text.Lines[text.Lines.Count - 1].ToString()).ConfigureAwait(false);
            }
        }

        internal async Task Initialize(SourceTextContainer sourceTextContainer,
            Action<DiagnosticsUpdatedArgs> onDiagnosticsUpdated, Action<SourceText> onTextUpdated,
            Action<ExceptionResultObject> onError,
            Func<TextSpan> getSelection, IDisposable viewDisposable)
        {
            _viewDisposable = viewDisposable;
            _onError = onError;
            _getSelection = getSelection;
            var roslynHost = MainViewModel.RoslynHost;
            // ReSharper disable once AssignNullToNotNullAttribute
            DocumentId = roslynHost.AddDocument(sourceTextContainer, _workingDirectory, onDiagnosticsUpdated,
                onTextUpdated);
        }

        public DocumentId DocumentId { get; private set; }

        public MainViewModel MainViewModel { get; }
        public ICommandProvider CommandProvider { get; }

        public string Title => Document != null && !Document.IsAutoSaveOnly ? Document.Name : "New";

        public IActionCommand SaveCommand { get; }

        public IActionCommand RunCommand { get; }

        public IActionCommand FormatDocumentCommand { get; }

        public IActionCommand CommentSelectionCommand { get; }

        public IActionCommand UncommentSelectionCommand { get; }

        public IActionCommand RenameSymbolCommand { get; }

        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    _dispatcher.InvokeAsync(() => RunCommand.RaiseCanExecuteChanged());
                }
            }
        }

        private async Task Run()
        {
            var selectedSpan = _getSelection();
            string code;
            bool echo;
            if (selectedSpan.Length > 0)
            {
                code = await GetCode(selectedSpan, CancellationToken.None).ConfigureAwait(true);
                echo = true;
            }
            else
            {
                code = await GetCode(CancellationToken.None).ConfigureAwait(true);
                echo = false;
            }

            await Run(code, echo).ConfigureAwait(false);
        }

        private Task Run(string code, bool echo)
        {
            Reset();
            SetIsRunning(true);

            try
            {
                var cancellationToken = _cts.Token;
                return ScriptEngine.Execute(code, echo, cancellationToken);

            }
            finally
            {
                SetIsRunning(false);
            }
        }

        private async Task<string> GetCode(CancellationToken cancellationToken)
        {
            return (await MainViewModel.RoslynHost.GetDocument(DocumentId).GetTextAsync(cancellationToken)
                .ConfigureAwait(false)).ToString();
        }

        private async Task<string> GetCode(TextSpan codeSpan, CancellationToken cancellationToken)
        {
            var text = await MainViewModel.RoslynHost.GetDocument(DocumentId).GetTextAsync(cancellationToken).ConfigureAwait(false);
            return text.GetSubText(codeSpan).ToString();
        }

        private void Reset()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();
        }

        public async Task<string> LoadText()
        {
            if (Document == null)
            {
                return string.Empty;
            }
            using (var fileStream = new StreamReader(Document.Path))
            {
                return await fileStream.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        public void Close()
        {
            _viewDisposable?.Dispose();
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            private set { SetProperty(ref _isDirty, value); }
        }

        public CSharpScriptEngine ScriptEngine { get; set; }

        public event EventHandler EditorFocus;

        private void OnEditorFocus()
        {
            EditorFocus?.Invoke(this, EventArgs.Empty);
        }

        public void SetDirty()
        {
            IsDirty = true;
        }
    }
}