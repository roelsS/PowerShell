/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Provider;
using Dbg = System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// A command that commits a transaction.
    /// </summary>
    [Cmdlet(VerbsOther.Use, "Transaction", SupportsTransactions = true, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135271")]
    public class UseTransactionCommand : PSCmdlet
    {
        /// <summary>
        /// This parameter specifies the script block to run in the current
        /// PowerShell transaction
        /// </summary>
        [Parameter(Position = 0, Mandatory = true)]
        public ScriptBlock TransactedScript
        {
            get
            {
                return transactedScript;
            }
            set
            {
                transactedScript = value;
            }
        }
        private ScriptBlock transactedScript;

        /// <summary>
        /// Commits the current transaction
        /// </summary>
        protected override void EndProcessing ()
        {
            using(CurrentPSTransaction)
            {
                try
                {
                    var emptyArray = Utils.EmptyArray<object>();
                    transactedScript.InvokeUsingCmdlet(
                        contextCmdlet:         this,
                        useLocalScope:         false,
                        errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToCurrentErrorPipe, 
                        dollarUnder:           null,
                        input:                 emptyArray,
                        scriptThis:            AutomationNull.Value,
                        args:                  emptyArray);
                }
                catch(Exception e)
                {
                    // Catch-all OK. This is a third-party call-out.
                    CommandProcessorBase.CheckForSevereException(e);

                    ErrorRecord errorRecord = new ErrorRecord(e, "TRANSACTED_SCRIPT_EXCEPTION", ErrorCategory.NotSpecified, null);

                    // The "transaction timed out" exception is
                    // exceedingly obtuse. We clarify things here.
                    bool isTimeoutException = false;
                    Exception tempException = e;
                    while(tempException != null)
                    {
                        if(tempException is System.TimeoutException)
                        {
                            isTimeoutException = true;
                            break;
                        }

                        tempException = tempException.InnerException;
                    }

                    if(isTimeoutException)
                    {
                        errorRecord = new ErrorRecord(
                            new InvalidOperationException(
                                TransactionResources.TransactionTimedOut),
                            "TRANSACTION_TIMEOUT",
                            ErrorCategory.InvalidOperation,
                            e);
                    }

                    WriteError(errorRecord);
                }
            }
        }

    } // CommitTransactionCommand

} // namespace Microsoft.PowerShell.Commands

