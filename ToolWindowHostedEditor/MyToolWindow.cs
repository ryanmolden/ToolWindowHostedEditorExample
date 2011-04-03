using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace ToolWindowHostedEditor
{
    [Guid("9f8b4a4a-6502-4607-91f3-fdb7be177bd1")]
    public class MyToolWindow : ToolWindowPane, 
                                IOleCommandTarget, 
                                IVsFindTarget, 
                                IVsFindTarget2
    {
        #region Constants

        private const int WM_KEYFIRST = 0x0100;
        private const int WM_KEYLAST = 0x0109;

        #endregion

        #region Private Fields

        private EditorHostControl control;
        private IOleCommandTarget cachedEditorCommandTarget;
        private IVsTextView textView;
        private IVsCodeWindow codeWindow;
        private IVsInvisibleEditor invisibleEditor;
        private IVsFindTarget cachedEditorFindTarget;
        private Microsoft.VisualStudio.OLE.Interop.IServiceProvider cachedOleServiceProvider;

        #endregion

        #region Ctor

        public MyToolWindow() : base(null)
        {
            this.Caption = Resources.ToolWindowTitle;
            this.BitmapResourceID = 301;
            this.BitmapIndex = 1;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Method to call to cause us to place the file at the given path into our hosted editor.
        /// </summary>
        public void SetDisplayedFile(string filePath)
        {
            ClearOldEditor();

            //Get an invisible editor over the file, this makes it much easier than having to manually figure out the right content type, 
            //language service, and it will automatically associate the document with its owning project, meaning we will get intellisense
            //in our editor with no extra work.
            IVsInvisibleEditorManager invisibleEditorManager = (IVsInvisibleEditorManager)GetService(typeof(SVsInvisibleEditorManager));
            ErrorHandler.ThrowOnFailure(invisibleEditorManager.RegisterInvisibleEditor(filePath,
                                                                                       pProject: null,
                                                                                       dwFlags: (uint)_EDITORREGFLAGS.RIEF_ENABLECACHING,
                                                                                       pFactory: null,
                                                                                       ppEditor: out this.invisibleEditor));

            //The doc data is the IVsTextLines that represents the in-memory version of the file we opened in our invisibe editor, we need
            //to extract that so that we can create our real (visible) editor.
            IntPtr docDataPointer = IntPtr.Zero;
            Guid guidIVSTextLines = typeof(IVsTextLines).GUID;
            ErrorHandler.ThrowOnFailure(this.invisibleEditor.GetDocData(fEnsureWritable: 1, riid: ref guidIVSTextLines, ppDocData: out docDataPointer));
            try
            {
                IVsTextLines docData = (IVsTextLines)Marshal.GetObjectForIUnknown(docDataPointer);

                //Get the component model so we can request the editor adapter factory which we can use to spin up an editor instance.
                IComponentModel componentModel = (IComponentModel)GetService(typeof(SComponentModel));
                IVsEditorAdaptersFactoryService editorAdapterFactoryService = componentModel.GetService<IVsEditorAdaptersFactoryService>();

                //Create a code window adapter.
                this.codeWindow = editorAdapterFactoryService.CreateVsCodeWindowAdapter(OleServiceProvider);

                //Associate our IVsTextLines with our new code window.
                ErrorHandler.ThrowOnFailure(this.codeWindow.SetBuffer((IVsTextLines)docData));

                //Get our text view for our editor which we will use to get the WPF control that hosts said editor.
                ErrorHandler.ThrowOnFailure(this.codeWindow.GetPrimaryView(out this.textView));

                //Get our WPF host from our text view (from our code window).
                IWpfTextViewHost textViewHost = editorAdapterFactoryService.GetWpfTextViewHost(this.textView);

                Debug.Assert(this.control != null);

                //We already have an open window, so just insert this editor in place of the old one.
                this.control.InsertNewEditor(textViewHost.HostControl);
            }
            finally
            {
                if (docDataPointer != IntPtr.Zero)
                {
                    //Release the doc data from the invisible editor since it gave us a ref-counted copy.
                    Marshal.Release(docDataPointer);
                }
            }
        }

        #endregion

        #region Protected Overrides

        /// <summary>
        /// Set our Content property to be an initially empty version of our editor hosting control.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            base.Content = this.control = new EditorHostControl(new TextBlock() { Text = "No Editorz Yet!!", 
                                                                                  HorizontalAlignment = HorizontalAlignment.Center, 
                                                                                  VerticalAlignment=VerticalAlignment.Center });
        }

        /// <summary>
        /// Preprocess input (keyboard) messages in order to translate them to editor commands if they map. Since our tool window is NOT an
        /// editor the shell won't consider the editor keybindings when doing its usual input pre-translation. We could either set our
        /// window frames InheritKeyBindings property to point to the std editor factory GUID OR we can do this. I chose this method as
        /// it allows us to have the editor keybindings active ONLY when the focus is in the editor, that way we won't have editor
        /// keybindings active in our window UNLESS the editor has focus, which is what we want.
        /// </summary>
        protected override bool PreProcessMessage(ref System.Windows.Forms.Message m)
        {
            //Only try and pre-process keyboard input messages, all others are not interesting to us.
            if (m.Msg >= WM_KEYFIRST && m.Msg <= WM_KEYLAST)
            {
                //Only attempt to do the input -> command mapping if focus is inside our hosted editor.
                if (this.control.IsKeyboardFocusWithin)
                {
                    IVsFilterKeys2 filterKeys = (IVsFilterKeys2)GetService(typeof(SVsFilterKeys));
                    MSG oleMSG = new MSG() { hwnd = m.HWnd, lParam = m.LParam, wParam = m.WParam, message = (uint)m.Msg };

                    //Ask the shell to do the command mapping for us and fire off the command if it succeeds with that mapping. We pass no 'custom' scopes
                    //(third and fourth argument) because we pass VSTAEXF_UseTextEditorKBScope to indicate we want the shell to apply the text editor
                    //command scope to this call.
                    Guid cmdGuid;
                    uint cmdId;
                    int fTranslated;
                    int fStartsMultiKeyChord;
                    int res = filterKeys.TranslateAcceleratorEx(new MSG[] { oleMSG },
                                                                (uint)(__VSTRANSACCELEXFLAGS.VSTAEXF_UseTextEditorKBScope),
                                                                0 /*scope count*/,
                                                                new Guid[0] /*scopes*/,
                                                                out cmdGuid,
                                                                out cmdId,
                                                                out fTranslated,
                                                                out fStartsMultiKeyChord);

                    if (fStartsMultiKeyChord == 0) 
                    {
                        //HACK: Work around a bug in TranslateAcceleratorEx that will report it DIDN'T do the command mapping 
                        //when in fact it did :( Problem has been fixed (since I found it while writing this code), but in the 
                        //mean time we need to successfully eat keystrokes that have been mapped to commands and dispatched, 
                        //we DON'T want them to continue on to Translate/Dispatch. "Luckily" asking TranslateAcceleratorEx to
                        //do the mapping WITHOUT firing the command will give us the right result code to indicate if the command
                        //mapped or not, unfortunately we can't always do this as it would break key-chords as it causes the shell 
                        //to not remember the first input match of a multi-part chord, hence the reason we ONLY hit this block if 
                        //it didn't tell us the input IS part of key-chord.
                        res = filterKeys.TranslateAcceleratorEx(new MSG[] { oleMSG },
                                                                (uint)(__VSTRANSACCELEXFLAGS.VSTAEXF_NoFireCommand | __VSTRANSACCELEXFLAGS.VSTAEXF_UseTextEditorKBScope),
                                                                0,
                                                                new Guid[0],
                                                                out cmdGuid,
                                                                out cmdId,
                                                                out fTranslated,
                                                                out fStartsMultiKeyChord);

                        return (res == VSConstants.S_OK);
                    }

                    //We return true (that we handled the input message) if we managed to map it to a command OR it was the 
                    //beginning of a multi-key chord, anything else should continue on with normal processing.
                    return ((res == VSConstants.S_OK) || (fStartsMultiKeyChord != 0));
                }
            }

            return base.PreProcessMessage(ref m);
        }

        #endregion

        #region IOleCommandTarget Members

        /// <summary>
        /// When our tool window is active it will be the 'focus command target' of the shell's command route, as such we need to handle any
        /// commands we want here and forward the rest to the editor (since most all typing is translated into a command for the editor to
        /// deal with).
        /// </summary>
        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (this.control.IsKeyboardFocusWithin && (EditorCommandTarget != null))
            {
                int res = EditorCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                return res;
            }

            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        /// <summary>
        /// When our tool window is active it will be the 'focus command target' of the shell's command route, as such we need to set the state
        /// of any commands we want here and forward the rest to the editor (since most all typing is translated into a command for the editor to
        /// deal with).
        /// </summary>
        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (this.control.IsKeyboardFocusWithin && (EditorCommandTarget != null))
            {
                return EditorCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }

            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        #endregion

        #region IVsFindTarget Members

        public int Find(string pszSearch, uint grfOptions, int fResetStartPoint, IVsFindHelper pHelper, out uint pResult)
        {
            return EditorFindTarget.Find(pszSearch, grfOptions, fResetStartPoint, pHelper, out pResult);
        }

        public int GetCapabilities(bool[] pfImage, uint[] pgrfOptions)
        {
            return EditorFindTarget.GetCapabilities(pfImage, pgrfOptions);
        }

        public int GetCurrentSpan(TextSpan[] pts)
        {
            return EditorFindTarget.GetCurrentSpan(pts);
        }

        public int GetFindState(out object ppunk)
        {
            return EditorFindTarget.GetFindState(out ppunk);
        }

        public int GetMatchRect(RECT[] prc)
        {
            return EditorFindTarget.GetMatchRect(prc);
        }

        public int GetProperty(uint propid, out object pvar)
        {
            return EditorFindTarget.GetProperty(propid, out pvar);
        }

        public int GetSearchImage(uint grfOptions, IVsTextSpanSet[] ppSpans, out IVsTextImage ppTextImage)
        {
            return EditorFindTarget.GetSearchImage(grfOptions, ppSpans, out ppTextImage);
        }

        public int MarkSpan(TextSpan[] pts)
        {
            return EditorFindTarget.MarkSpan(pts);
        }

        public int NavigateTo(TextSpan[] pts)
        {
            return EditorFindTarget.NavigateTo(pts);
        }

        public int NotifyFindTarget(uint notification)
        {
            return EditorFindTarget.NotifyFindTarget(notification);
        }

        public int Replace(string pszSearch, string pszReplace, uint grfOptions, int fResetStartPoint, IVsFindHelper pHelper, out int pfReplaced)
        {
            return EditorFindTarget.Replace(pszSearch, pszReplace, grfOptions, fResetStartPoint, pHelper, out pfReplaced);
        }

        public int SetFindState(object pUnk)
        {
            return EditorFindTarget.SetFindState(pUnk);
        }
        #endregion

        #region IVsFindTarget2 Members

        public int NavigateTo2(IVsTextSpanSet pSpans, TextSelMode iSelMode)
        {
            return ((IVsFindTarget2)EditorFindTarget).NavigateTo2(pSpans, iSelMode);
        }

        #endregion

        #region Private Properties

        /// <summary>
        /// The IOleCommandTarget for the editor that our tool window will forward all command requests to when it is the active tool window
        /// and the editor we are hosting has keyboard focus.
        /// </summary>
        private IOleCommandTarget EditorCommandTarget
        {
            get
            {
                return (this.cachedEditorCommandTarget ?? (this.cachedEditorCommandTarget = this.textView as IOleCommandTarget));
            }
        }

        /// <summary>
        /// The IVsFindTarget for the editor that our tool window will forward all find releated requests to when it is the active tool window
        /// and the editor we are hosting has keyboard focus.
        /// </summary>
        private IVsFindTarget EditorFindTarget
        {
            get
            {
                return (this.cachedEditorFindTarget ?? (this.cachedEditorFindTarget = this.textView as IVsFindTarget));
            }
        }

        /// <summary>
        /// The shell's service provider as an OLE service provider (needed to create the editor bits).
        /// </summary>
        private Microsoft.VisualStudio.OLE.Interop.IServiceProvider OleServiceProvider
        {
            get
            {
                if (this.cachedOleServiceProvider == null)
                {
                    //ServiceProvider.GlobalProvider is a System.IServiceProvider, but the editor pieces want an OLE.IServiceProvider, luckily the
                    //global provider is also IObjectWithSite and we can use that to extract its underlying (OLE) IServiceProvider object.
                    IObjectWithSite objWithSite = (IObjectWithSite)ServiceProvider.GlobalProvider;

                    Guid interfaceIID = typeof(Microsoft.VisualStudio.OLE.Interop.IServiceProvider).GUID;
                    IntPtr rawSP;
                    objWithSite.GetSite(ref interfaceIID, out rawSP);
                    try
                    {
                        if (rawSP != IntPtr.Zero)
                        {
                            //Get an RCW over the raw OLE service provider pointer.
                            this.cachedOleServiceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)Marshal.GetObjectForIUnknown(rawSP);
                        }
                    }
                    finally
                    {
                        if (rawSP != IntPtr.Zero)
                        {
                            //Release the raw pointer we got from IObjectWithSite so we don't cause leaks.
                            Marshal.Release(rawSP);
                        }
                    }
                }

                return this.cachedOleServiceProvider;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Cleans up an existing editor if we are about to put a new one in place, used to close down the old editor bits as well as
        /// nulling out any cached objects that we have that came from the now dead editor.
        /// </summary>
        private void ClearOldEditor()
        {
            if (this.codeWindow != null)
            {
                this.codeWindow.Close();
                this.codeWindow = null;
            }

            if (this.textView != null)
            {
                this.textView.CloseView();
                this.textView = null;
            }

            this.cachedEditorCommandTarget = null;
            this.cachedEditorFindTarget = null;
            this.invisibleEditor = null;
        }

        #endregion
    }
}