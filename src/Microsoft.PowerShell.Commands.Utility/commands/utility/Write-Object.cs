/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    #region WriteOutputCommand
    /// <summary>
    /// This class implements Write-output command
    /// 
    /// </summary>
    [Cmdlet("Write", "Output", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113427", RemotingCapability = RemotingCapability.None)]
    public sealed class WriteOutputCommand : PSCmdlet
    {
        private PSObject[] inputObjects = null;

        /// <summary>
        /// Holds the list of objects to be Written
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true, ValueFromRemainingArguments = true)]
        [AllowNull]
        [AllowEmptyCollection]
        public PSObject[] InputObject
        {
            get { return inputObjects; }
            set { inputObjects = value; }
        }

        /// <summary>
        /// Prevents Write-Output from unravelling collections passed to the InputObject
        /// parameter.
        /// </summary>
        [Parameter()]
        public SwitchParameter NoEnumerate
        {
            get;
            set;
        }

        /// <summary>
        /// This method implements the ProcessRecord method for Write-output command
        /// </summary>
        protected override void ProcessRecord()
        {
            if (null == inputObjects)
            {
                WriteObject(inputObjects);
                return;
            }

            bool enumerate = true;
            if (NoEnumerate.IsPresent)
            {
                enumerate = false;
            }
            foreach (PSObject inputObject in inputObjects) // compensate for ValueFromRemainingArguments
            {
                WriteObject(inputObject, enumerate);
            }
        }//processrecord
    }//WriteOutputCommand
    #endregion
}
