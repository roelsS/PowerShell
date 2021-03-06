/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics; // Process class
using System.ComponentModel; // Win32Exception
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Threading;
using System.Management;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Security.AccessControl;
using Dbg = System.Management.Automation;


namespace Microsoft.PowerShell.Commands
{

    #region Get-HotFix

    /// <summary>
    /// Cmdlet for Get-Hotfix Proxy
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "HotFix", DefaultParameterSetName = "Default",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135217", RemotingCapability = RemotingCapability.SupportedByCommand)]
    [OutputType(@"System.Management.ManagementObject#root\cimv2\Win32_QuickFixEngineering")]
    public sealed class GetHotFixCommand : PSCmdlet, IDisposable
    {
        #region Parameters

        /// <summary>
        /// Specifies the HotFixID. Unique identifier associated with a particular update.
        /// </summary>
        [Parameter(Position = 0,ParameterSetName="Default")]
        [ValidateNotNullOrEmpty]
        [Alias("HFID")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] Id
        {
            get { return _id; }
            set
            {
                _id = value;
            }
        }
        private string[] _id;

        /// <summary>
        /// To search on description of Hotfixes
        /// </summary>
        [Parameter(ParameterSetName = "Description")]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] Description
        {
            get { return _description; }
            set
            {
                _description = value;
            }
        }
        private string[] _description;

        /// <summary>
        /// Parameter to pass the Computer Name
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [Alias("CN", "__Server", "IPAddress")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ComputerName
        {
            get { return _computername; }
            set
            {
                _computername = value;
            }
        }
        private string[] _computername = new string[] { "localhost" };

        /// <summary>
        /// Parameter to pass the Credentials.
        /// </summary>
        [Parameter]
        [Credential]
        [ValidateNotNullOrEmpty]
        public PSCredential Credential
        {
            get { return _credential; }
            set
            {
                _credential = value;
            }
        }
        private PSCredential _credential;

        #endregion Parameters

        #region Overrides

        
        private ManagementObjectSearcher searchProcess;

        private bool inputContainsWildcard = false;
        /// <summary>
        /// Get the List of HotFixes installed on the Local Machine.
        /// </summary>
        protected override void BeginProcessing()
        {
            foreach (string computer in _computername)
            {
                bool foundRecord = false;
                StringBuilder QueryString = new StringBuilder();
                ConnectionOptions conOptions = ComputerWMIHelper.GetConnectionOptions(AuthenticationLevel.Packet, ImpersonationLevel.Impersonate, this.Credential);
                ManagementScope scope = new ManagementScope(ComputerWMIHelper.GetScopeString(computer, ComputerWMIHelper.WMI_Path_CIM), conOptions);
                scope.Connect();
                if (_id != null)
                {
                    QueryString.Append("Select * from Win32_QuickFixEngineering where (");
                    for (int i = 0; i <= _id.Length - 1; i++)
                    {
                        QueryString.Append("HotFixID= '");
                        QueryString.Append(_id[i].ToString().Replace("'", "\\'"));
                        QueryString.Append("'");
                        if (i < _id.Length - 1)
                            QueryString.Append(" Or ");
                    }
                    QueryString.Append(")");
                }
                else
                {
                    QueryString.Append("Select * from Win32_QuickFixEngineering");
                    foundRecord = true;
                }
                searchProcess = new ManagementObjectSearcher(scope, new ObjectQuery(QueryString.ToString()));
                foreach (ManagementObject obj in searchProcess.Get())
                {
                    if (_description != null)
                    {
                        if (!FilterMatch(obj))
                            continue;
                    }
                    else
                    {
                        inputContainsWildcard = true;
                    }

                    // try to translate the SID to a more friendly username
                    // just stick with the SID if anything goes wrong
                    string installed = (string)obj["InstalledBy"];
                    if (!String.IsNullOrEmpty(installed))
                    {
                        try
                        {
                            SecurityIdentifier secObj = new SecurityIdentifier(installed);
                            obj["InstalledBy"] = secObj.Translate(typeof(NTAccount)); ;
                        }
                        catch (IdentityNotMappedException) // thrown by SecurityIdentifier.Translate
                        {
                        }
                        catch (SystemException e) // thrown by SecurityIdentifier.constr
                        {
                            CommandsCommon.CheckForSevereException(this, e); 
                        }
                        //catch (ArgumentException) // thrown (indirectly) by SecurityIdentifier.constr (on XP only?)
                        //{ catch not needed - this is already caught as SystemException
                        //}
                        //catch (PlatformNotSupportedException) // thrown (indirectly) by SecurityIdentifier.Translate (on Win95 only?)
                        //{ catch not needed - this is already caught as SystemException
                        //}
                        //catch (UnauthorizedAccessException) // thrown (indirectly) by SecurityIdentifier.Translate 
                        //{ catch not needed - this is already caught as SystemException
                        //}
                    }

                    WriteObject(obj);
                    foundRecord = true;
                }
                if (!foundRecord && !inputContainsWildcard)
                {
                    Exception Ex = new ArgumentException(StringUtil.Format(HotFixResources.NoEntriesFound, computer));
                    WriteError(new ErrorRecord(Ex, "GetHotFixNoEntriesFound", ErrorCategory.ObjectNotFound, null));
                }
                if (searchProcess != null)
                {
                    this.Dispose();
                }
            }
        }//end of BeginProcessing method

        /// <summary>
        /// to implement ^C
        /// </summary>
        protected override void StopProcessing()
        {
            if (searchProcess != null)
            {
                searchProcess.Dispose();
            }       
        }
        #endregion Overrides

        #region "Private Methods"

        private bool FilterMatch(ManagementObject obj)
        {
            try
            {
                foreach (string desc in _description)
                {
                    WildcardPattern wildcardpattern = WildcardPattern.Get(desc, WildcardOptions.IgnoreCase);
                    if (wildcardpattern.IsMatch((string)obj["Description"]))
                    {
                        return true;
                    }
                    if (WildcardPattern.ContainsWildcardCharacters(desc))
                    {
                        inputContainsWildcard = true;
                    }
                }
            }
            catch (Exception e)
            { 
                CommandsCommon.CheckForSevereException(this, e);
                return false;
            }
            return false;
        }

        #endregion "Private Methods"

        #region "IDisposable Members"

        /// <summary>
        /// Dispose Method
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            // Use SupressFinalize in case a subclass
            // of this type implements a finalizer.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose Method.
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (searchProcess != null)
                {
                    searchProcess.Dispose();
                }
            }
        }

        #endregion "IDisposable Members"

    }//end class
    #endregion

}//Microsoft.Powershell.commands
