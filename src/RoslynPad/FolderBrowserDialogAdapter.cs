﻿using System.Composition;
using System.Linq;
using System.Windows;
using Avalon.Windows.Dialogs;
using RoslynPad.UI;

namespace RoslynPad
{
    [Export(typeof(IFolderBrowserDialog))]
    internal class FolderBrowserDialogAdapter : IFolderBrowserDialog
    {
        private readonly FolderBrowserDialog _dialog;

        public FolderBrowserDialogAdapter()
        {
            _dialog = new FolderBrowserDialog();
        }

        public bool ShowEditBox
        {
            get { return _dialog.ShowEditBox; }
            set { _dialog.ShowEditBox = value; }
        }

        public string SelectedPath
        {
            get { return _dialog.SelectedPath; }
            set { _dialog.SelectedPath = value; }
        }

        public bool? Show()
        {
            return _dialog.ShowDialog(Application.Current.Windows.OfType<MainWindow>().First());
        }
    }
}