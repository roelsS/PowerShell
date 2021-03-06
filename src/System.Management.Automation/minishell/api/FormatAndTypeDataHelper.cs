/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Management.Automation.Host;
using System.Collections;
using System.Collections.Concurrent;
using System.Management.Automation.Internal;

namespace System.Management.Automation.Runspaces
{
    internal class PSSnapInTypeAndFormatErrors
    {
        public string psSnapinName;
        // only one of fullPath or formatTable or typeData or typeDefinition should be specified..
        private string fullPath;
        private FormatTable formatTable;
        // typeData and isRemove should be used together
        private TypeData typeData;
        private bool isRemove;
        private ExtendedTypeDefinition typeDefinition;
        private ConcurrentBag<string> errors;

        internal PSSnapInTypeAndFormatErrors(string psSnapinName, string fullPath)
        {
            this.psSnapinName = psSnapinName;
            this.fullPath = fullPath;
            this.errors = new ConcurrentBag<string>();
        }

        internal PSSnapInTypeAndFormatErrors(string psSnapinName, FormatTable formatTable)
        {
            this.psSnapinName = psSnapinName;
            this.formatTable = formatTable;
            this.errors = new ConcurrentBag<string>();
        }

        internal PSSnapInTypeAndFormatErrors(string psSnapinName, TypeData typeData, bool isRemove)
        {
            this.psSnapinName = psSnapinName;
            this.typeData = typeData;
            this.isRemove = isRemove;
            this.errors = new ConcurrentBag<string>();
        }

        internal PSSnapInTypeAndFormatErrors(string psSnapinName, ExtendedTypeDefinition typeDefinition)
        {
            this.psSnapinName = psSnapinName;
            this.typeDefinition = typeDefinition;
            this.errors = new ConcurrentBag<string>();
        }

        internal ExtendedTypeDefinition FormatData { get { return typeDefinition; } }
        internal TypeData TypeData { get { return typeData; } }
        internal bool IsRemove { get { return isRemove; } }
        internal string FullPath { get { return fullPath; } }
        internal FormatTable FormatTable { get { return formatTable; } }
        internal ConcurrentBag<string> Errors 
        { 
            get { return errors; }
            set { errors = value; }
        }
        internal string PSSnapinName { get { return psSnapinName; } }
        internal bool FailToLoadFile;
    }

    internal static class FormatAndTypeDataHelper
    {
        private const string FileNotFound = "FileNotFound";
        private const string CannotFindRegistryKey = "CannotFindRegistryKey";
        private const string CannotFindRegistryKeyPath = "CannotFindRegistryKeyPath";
        private const string EntryShouldBeMshXml = "EntryShouldBeMshXml";
        private const string DuplicateFile = "DuplicateFile";
        internal const string ValidationException = "ValidationException";

        private static string GetBaseFolder(
            RunspaceConfiguration runspaceConfiguration, 
            Collection<string> independentErrors)
        {
            string returnValue = CommandDiscovery.GetShellPathFromRegistry(runspaceConfiguration.ShellId);
            if (returnValue == null)
            {
                returnValue = Path.GetDirectoryName(PsUtils.GetMainModule(System.Diagnostics.Process.GetCurrentProcess()).FileName);
            }
            else
            {
                returnValue = Path.GetDirectoryName(returnValue);
                if (!Directory.Exists(returnValue))
                {
                    string newReturnValue = Path.GetDirectoryName(typeof(FormatAndTypeDataHelper).GetTypeInfo().Assembly.Location);
                    string error = StringUtil.Format(TypesXmlStrings.CannotFindRegistryKeyPath, returnValue,
                        Utils.GetRegistryConfigurationPath(runspaceConfiguration.ShellId), "\\Path", newReturnValue);
                    independentErrors.Add(error);
                    returnValue = newReturnValue;
                }
            }
            return returnValue;
        }

        internal static Collection<PSSnapInTypeAndFormatErrors> GetFormatAndTypesErrors(
            RunspaceConfiguration runspaceConfiguration,
            PSHost host,
            IEnumerable<RunspaceConfigurationEntry> configurationEntryCollection,
            Collection<string> independentErrors,
            Collection<int> entryIndicesToRemove)
        {
            Collection<PSSnapInTypeAndFormatErrors> returnValue = new Collection<PSSnapInTypeAndFormatErrors>();

            string baseFolder = GetBaseFolder(runspaceConfiguration, independentErrors);
            var psHome = Utils.GetApplicationBase(Utils.DefaultPowerShellShellID);

            // this hashtable will be used to check whether this is duplicated file for types or formats. 
            HashSet<string> fullFileNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = -1;

            foreach (var configurationEntry in  configurationEntryCollection)
            {
                string fileName;
                string psSnapinName = configurationEntry.PSSnapIn == null ? runspaceConfiguration.ShellId : configurationEntry.PSSnapIn.Name;
                index++;
                var typeEntry = configurationEntry as TypeConfigurationEntry;
                if (typeEntry != null)
                {
                    fileName = typeEntry.FileName;

                    if(fileName == null)
                    {
                        returnValue.Add(new PSSnapInTypeAndFormatErrors(psSnapinName, typeEntry.TypeData, typeEntry.IsRemove));
                        continue;
                    }
                }
                else
                {
                    FormatConfigurationEntry formatEntry = (FormatConfigurationEntry)configurationEntry;
                    fileName = formatEntry.FileName;

                    if(fileName == null)
                    {
                        returnValue.Add(new PSSnapInTypeAndFormatErrors(psSnapinName, formatEntry.FormatData));
                        continue;
                    }
                }

                bool checkFileExists = configurationEntry.PSSnapIn == null || string.Equals(psHome, configurationEntry.PSSnapIn.AbsoluteModulePath, StringComparison.OrdinalIgnoreCase);
                bool needToRemoveEntry = false;
                string fullFileName = GetAndCheckFullFileName(psSnapinName, fullFileNameSet, baseFolder, fileName, independentErrors, ref needToRemoveEntry, checkFileExists);
                if (fullFileName == null)
                {
                    if (needToRemoveEntry)
                    {
                        entryIndicesToRemove.Add(index);
                    }
                    continue;
                }
                
                returnValue.Add(new PSSnapInTypeAndFormatErrors(psSnapinName, fullFileName));
            }
            return returnValue;
        }

        private static string GetAndCheckFullFileName(
            string psSnapinName, 
            HashSet<string> fullFileNameSet, 
            string baseFolder, 
            string baseFileName, 
            Collection<string> independentErrors, 
            ref bool needToRemoveEntry,
            bool checkFileExists)
        {
            string retValue = Path.IsPathRooted(baseFileName) ? baseFileName : Path.Combine(baseFolder, baseFileName);

            if (checkFileExists && !File.Exists(retValue))
            {
                string error = StringUtil.Format(TypesXmlStrings.FileNotFound, psSnapinName, retValue);
                independentErrors.Add(error);
                return null;
            }

            if (fullFileNameSet.Contains(retValue))
            {
                // Do not add Errors as we want loading of type/format files to be idempotent.
                // Just mark as Duplicate so the duplicate entry gets removed
                needToRemoveEntry = true;
                return null;
            }

            if (!retValue.EndsWith(".ps1xml", StringComparison.OrdinalIgnoreCase))
            {
                string error = StringUtil.Format(TypesXmlStrings.EntryShouldBeMshXml, psSnapinName, retValue);
                independentErrors.Add(error);
                return null;
            }

            fullFileNameSet.Add(retValue);
            return retValue;
        }

        internal static void ThrowExceptionOnError(
            string errorId, 
            Collection<string> independentErrors,
            Collection<PSSnapInTypeAndFormatErrors> PSSnapinFilesCollection, 
            RunspaceConfigurationCategory category)
        {
            Collection<string> errors = new Collection<string>();
            if (independentErrors != null)
            {
                foreach (string error in independentErrors)
                {
                    errors.Add(error);
                }
            }

            foreach (PSSnapInTypeAndFormatErrors PSSnapinFiles in PSSnapinFilesCollection)
            {
                foreach (string error in PSSnapinFiles.Errors)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count == 0)
            {
                return;
            }

            StringBuilder allErrors = new StringBuilder();

            allErrors.Append('\n');
            foreach (string error in errors)
            {
                allErrors.Append(error);
                allErrors.Append('\n');
            }

            string message = "";
            if (category == RunspaceConfigurationCategory.Types)
            {
                message =
                    StringUtil.Format(ExtendedTypeSystem.TypesXmlError, allErrors.ToString());
            }
            else if (category == RunspaceConfigurationCategory.Formats)
            {
                message = StringUtil.Format(FormatAndOutXmlLoadingStrings.FormatLoadingErrors, allErrors.ToString());
            }
            RuntimeException ex = new RuntimeException(message);
            ex.SetErrorId(errorId);
            throw ex;
        }

        internal static void ThrowExceptionOnError(
            string errorId, 
            ConcurrentBag<string> errors, 
            RunspaceConfigurationCategory category)
        {
            if (errors.Count == 0)
            {
                return;
            }

            StringBuilder allErrors = new StringBuilder();

            allErrors.Append('\n');
            foreach (string error in errors)
            {
                allErrors.Append(error);
                allErrors.Append('\n');
            }

            string message = "";
            if (category == RunspaceConfigurationCategory.Types)
            {
                message =
                    StringUtil.Format(ExtendedTypeSystem.TypesXmlError, allErrors.ToString());
            }
            else if (category == RunspaceConfigurationCategory.Formats)
            {
                message = StringUtil.Format(FormatAndOutXmlLoadingStrings.FormatLoadingErrors, allErrors.ToString());
            }
            RuntimeException ex = new RuntimeException(message);
            ex.SetErrorId(errorId);
            throw ex;
        }
    }
}



