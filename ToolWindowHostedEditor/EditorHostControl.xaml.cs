using System.Windows.Controls;

namespace ToolWindowHostedEditor
{
    public partial class EditorHostControl : UserControl
    {
        public EditorHostControl(object editor)
        {
            InitializeComponent();            
            EditorHost.Content = editor;
        }

        public void InsertNewEditor(object editor)
        {
            EditorHost.Content = editor;
        }
    }
}