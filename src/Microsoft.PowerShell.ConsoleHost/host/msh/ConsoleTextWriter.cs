/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/



using System;
using System.Text;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Internal;
using Microsoft.PowerShell;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


using Dbg = System.Management.Automation.Diagnostics;
using ConsoleHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;
using HRESULT = System.UInt32;
using DWORD = System.UInt32;
using NakedWin32Handle = System.IntPtr;



namespace Microsoft.PowerShell
{
    internal
    class ConsoleTextWriter : TextWriter
    {
        internal
        ConsoleTextWriter(ConsoleHostUserInterface ui)
            :
            base(System.Globalization.CultureInfo.CurrentCulture)
        {
            Dbg.Assert(ui != null, "ui needs a value");

            this.ui = ui;
        }



        public override
        Encoding
        Encoding
        {
            get
            {
                return null;
            }
        }



        public override
        void
        Write(string value)
        {
            ui.WriteToConsole(value, true);
        }



        public override
        void
        WriteLine(string value)
        {
            this.Write(value + ConsoleHostUserInterface.Crlf);
        }



        public override
        void
        Write(Boolean b)
        {
            this.Write(b.ToString());
        }



        public override
        void
        Write(Char c)
        {
            this.Write(new String(c, 1));
        }



        public override
        void
        Write(Char[] a)
        {
            this.Write(new String(a));
        }



        private ConsoleHostUserInterface ui;
    }

}   // namespace 


