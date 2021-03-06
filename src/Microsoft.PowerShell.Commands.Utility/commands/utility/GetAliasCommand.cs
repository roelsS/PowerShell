/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// The implementation of the "get-alias" cmdlet
    /// </summary>
    /// 
    [Cmdlet(VerbsCommon.Get, "Alias", DefaultParameterSetName = "Default", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113306")]
    [OutputType(typeof(AliasInfo))]
    public class GetAliasCommand : PSCmdlet
    {
        #region Parameters

        /// <summary>
        /// The Name parameter for the command
        /// </summary>
        /// 
        [Parameter(ParameterSetName = "Default", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
        public string[] Name
        {
            get
            {
                return names;
            }
            set
            {
                if (value == null)
                {
                    names = new string[] { "*" };
                }
                else
                {
                    names = value;
                }
            }
        }
        private string[] names = new string[] { "*" };

        /// <summary>
        /// The Exclude parameter for the command
        /// </summary>
        /// 
        [Parameter]
        public string[] Exclude
        {
            get
            {
                return excludes;
            }
            set
            {
                if (value == null)
                {
                    excludes = new string[0];
                }
                else
                {
                    excludes = value;
                }
            }
        }
        private string[] excludes = new string[0];

        /// <summary>
        /// The scope parameter for the command determines
        /// which scope the aliases are retrieved from.
        /// </summary>
        /// 
        [Parameter]
        public string Scope
        {
            get
            {
                return scope;
            }

            set
            {
                scope = value;
            }
        }
        private string scope;

        /// <summary>
        /// Parameter definition to retrieve aliases based on their definitions.
        /// </summary>
        [Parameter(ParameterSetName = "Definition")]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public String[] Definition
        {
            get { return _definition; }
            set { _definition = value; }
        }
        private string[] _definition;


        #endregion Parameters

        #region Command code

        /// <summary>
        /// The main processing loop of the command.
        /// </summary>
        /// 
        protected override void ProcessRecord()
        {
            if (ParameterSetName.Equals("Definition"))
            {
                foreach (string defn in _definition)
                {
                    WriteMatches(defn, "Definition");
                }
            }
            else
            {
                foreach (string aliasName in names)
                {
                    WriteMatches(aliasName, "Default");
                }
            }//parameterset else                
        } // ProcessRecord
        #endregion Command code

        private void WriteMatches(string value, string parametersetname)
        {

            // First get the alias table (from the proper scope if necessary)
            IDictionary<string, AliasInfo> aliasTable = null;

            //get the command origin
            CommandOrigin origin = MyInvocation.CommandOrigin;
            string displayString = "name";
            if (!String.IsNullOrEmpty(scope))
            {
                // This can throw PSArgumentException and PSArgumentOutOfRangeException
                // but just let them go as this is terminal for the pipeline and the
                // exceptions are already properly adorned with an ErrorRecord.

                aliasTable = SessionState.Internal.GetAliasTableAtScope(scope);
            }
            else
            {
                aliasTable = SessionState.Internal.GetAliasTable();
            }



            bool matchfound = false;
            bool ContainsWildcard = WildcardPattern.ContainsWildcardCharacters(value);
            WildcardPattern wcPattern = WildcardPattern.Get(value, WildcardOptions.IgnoreCase);

            // exlucing patter for Default paramset.
            Collection<WildcardPattern> excludePatterns =
                      SessionStateUtilities.CreateWildcardsFromStrings(
                          excludes,
                          WildcardOptions.IgnoreCase);

            List<AliasInfo> results = new List<AliasInfo>();
            foreach (KeyValuePair<string, AliasInfo> tableEntry in aliasTable)
            {
                if (parametersetname.Equals("Definition", StringComparison.OrdinalIgnoreCase))
                {
                    displayString = "definition";
                    if (!wcPattern.IsMatch(tableEntry.Value.Definition))
                    {
                        continue;
                    }
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(tableEntry.Value.Definition, excludePatterns, false))
                    {
                        continue;
                    }

                }
                else
                {
                    if (!wcPattern.IsMatch(tableEntry.Key))
                    {
                        continue;
                    }
                    //excludes pattern
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(tableEntry.Key, excludePatterns, false))
                    {
                        continue;
                    }

                }
                if (ContainsWildcard)
                {
                    // Only write the command if it is visible to the requestor
                    if (SessionState.IsVisible(origin, tableEntry.Value))
                    {
                        matchfound = true;
                        results.Add(tableEntry.Value);
                    }
                }
                else
                {
                    // For specifically named elements, generate an error for elements that aren't visible...
                    try
                    {
                        SessionState.ThrowIfNotVisible(origin, tableEntry.Value);
                        results.Add(tableEntry.Value);
                        matchfound = true;
                    }
                    catch (SessionStateException sessionStateException)
                    {
                        WriteError(
                            new ErrorRecord(
                                sessionStateException.ErrorRecord,
                                sessionStateException));
                        // Even though it resulted in an error, a result was found
                        // so we don't want to generate the nothing found error
                        // at the end...
                        matchfound = true;
                        continue;
                    }
                }

            }

            results.Sort(
                delegate(AliasInfo left, AliasInfo right)
                {
                    return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
                });
            foreach (AliasInfo alias in results)
            {
                this.WriteObject(alias);
            }

            if (!matchfound && !ContainsWildcard && (excludePatterns == null || excludePatterns.Count == 0))
            {
                // Need to write an error if the user tries to get an alias
                // tat doesn't exist and they are not globbing.

                ItemNotFoundException itemNotFound = new ItemNotFoundException(StringUtil.Format(AliasCommandStrings.NoAliasFound, displayString, value));
                ErrorRecord er = new ErrorRecord(itemNotFound, "ItemNotFoundException", ErrorCategory.ObjectNotFound, value);
                WriteError(er);
            }
        }
    } // class GetAliasCommand
}//Microsoft.PowerShell.Commands

