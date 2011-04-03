using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ToolWindowHostedEditor
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(MyToolWindow))]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string)] /*auto load when a solution is opened so we can have our commands QueryStatus handler running.*/
    [Guid(GuidList.guidToolWindowHostedEditorPkgString)]
    public sealed class ToolWindowHostedEditorPackage : Package
    {
        #region Package Members

        protected override void Initialize()
        {            
            base.Initialize();

            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (mcs != null)
            {
                // Register a command handler to show our tool window.
                CommandID toolwndCommandID = new CommandID(GuidList.guidToolWindowHostedEditorCmdSet, (int)PkgCmdIDList.cmdidOpenInEmbeddedEditor);
                OleMenuCommand menuToolWin = new OleMenuCommand(ShowToolWindow, toolwndCommandID);

                //Add a BeforeQueryStatus handler so we can hide/disable our command if the right click happened on a node isn't a file type.
                menuToolWin.BeforeQueryStatus += ShowToolWindowQS;
                
                mcs.AddCommand( menuToolWin );
            }
        }

#endregion

        #region Private Methods

        /// <summary>
        /// QueryStatus handler for our command, makes it visible and enabled only if a physical file type is selected in the active hierarchy when
        /// QueryStatus is performed (right before showing the context menu our command is located on).
        /// </summary>
        private void ShowToolWindowQS(object sender, EventArgs args) 
        {
            OleMenuCommand command = (OleMenuCommand)sender;

            //Grab the active hierarchy and selected item from the shell.
            IVsHierarchy activeHierarchy;
            uint itemId;
            GetActiveHierarchyAndSelection(out activeHierarchy, out itemId);

            //If there is an active hierarchy (and it has a selection, and the selection isn't the hierarchy node itself)
            if (IsValidSelection(activeHierarchy, itemId))
            {
                //Check if the selected item is a physical file by checking its TypeGuid.
                Guid itemTypeGuid;
                int res = activeHierarchy.GetGuidProperty(itemId, (int)__VSHPROPID.VSHPROPID_TypeGuid, out itemTypeGuid);

                //We are enabled as long as the selected item is a physical file. This doesn't mean we can open every 
                //physical file (i.e. invoke the command on an image file and you get an error), but making it even 
                //more picky is left as an excercise for the reader.
                command.Enabled = command.Visible = (itemTypeGuid == VSConstants.GUID_ItemType_PhysicalFile);
                return;
            }

            command.Visible = false;
            command.Enabled = false;
        }

        /// <summary>
        /// Shows our tool window over the currently selected item of the active hierarchy.
        /// </summary>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            OpenFileInEmbeddedEditor(GetPathOfCurrentlySelectedItem());
        }

        /// <summary>
        /// Opens the given file into our toolwindow placing it into the embedded editor (via the call to SetDisplayedFilePath on MyToolWindow).
        /// </summary>
        private void OpenFileInEmbeddedEditor(string filePath)
        {
            MyToolWindow window = (MyToolWindow)this.FindToolWindow(typeof(MyToolWindow), id: 0, create: true);
            window.SetDisplayedFile(filePath);

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        /// <summary>
        /// Asks the shell for the currently active hierarchy and the selected item of that hierarchy.
        /// </summary>
        private void GetActiveHierarchyAndSelection(out IVsHierarchy hierarchy, out uint selectedItemID)
        {
            IVsMonitorSelection selection = (IVsMonitorSelection)GetService(typeof(SVsShellMonitorSelection));

            IntPtr pHier;
            IVsMultiItemSelect mis;
            IntPtr pSC;

            ErrorHandler.ThrowOnFailure(selection.GetCurrentSelection(out pHier, out selectedItemID, out mis, out pSC));
            try
            {
                hierarchy = (IVsHierarchy)Marshal.GetObjectForIUnknown(pHier);
            }
            finally
            {
                if (pHier != IntPtr.Zero)
                {
                    Marshal.Release(pHier);
                }

                if (pSC != IntPtr.Zero)
                {
                    Marshal.Release(pSC);
                }
            }
        }

        /// <summary>
        /// We will consider the selection valid if the hierarchy is non-null, it has a selected item and the selected item isn't the hierarchy itself.
        /// </summary>
        private static bool IsValidSelection(IVsHierarchy hierarchy, uint itemId)
        {
            return (hierarchy != null && (itemId != (uint)VSConstants.VSITEMID.Nil) && (itemId != (uint)VSConstants.VSITEMID.Root));
        }

        /// <summary>
        /// Gets the path of the item that is currently selected in the solution explorer if the selected item is not a hierarchy node, if it is
        /// (or there is no selected item) this method returns null.
        /// </summary>
        private string GetPathOfCurrentlySelectedItem()
        {
            string filePath = null;

            IVsHierarchy activeHierarcy;
            uint itemID;
            GetActiveHierarchyAndSelection(out activeHierarcy, out itemID);
            if (IsValidSelection(activeHierarcy, itemID))
            {
                ErrorHandler.ThrowOnFailure(activeHierarcy.GetCanonicalName(itemID, out filePath));
            }

            return filePath;
        }

        #endregion
    }
}