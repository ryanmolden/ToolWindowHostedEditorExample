using System;

namespace ToolWindowHostedEditor
{
    static class GuidList
    {
        public const string guidToolWindowHostedEditorPkgString = "080018dc-fac4-4e25-bd72-1089dccde0bb";
        public const string guidToolWindowHostedEditorCmdSetString = "61cbbaf7-9ee3-4ddc-8a89-2b024b37fb9e";
        public const string guidToolWindowPersistanceString = "9f8b4a4a-6502-4607-91f3-fdb7be177bd1";

        public static readonly Guid guidToolWindowHostedEditorCmdSet = new Guid(guidToolWindowHostedEditorCmdSetString);
    };
}