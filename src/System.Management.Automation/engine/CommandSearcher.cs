/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation
{
    /// <summary>
    /// Used to enumerate the commands on the system that match the specified
    /// command name
    /// </summary>
    internal class CommandSearcher : IEnumerable<CommandInfo>, IEnumerator<CommandInfo>
    {
        /// <summary>
        /// Constructs a command searching enumerator that resolves the location 
        /// to a command using a standard algorithm.
        /// </summary>
        /// 
        /// <param name="commandName">
        /// The name of the command to look for.
        /// </param>
        /// 
        /// <param name="options">
        /// Determines which types of commands glob resolution of the name will take place on.
        /// </param>
        /// 
        /// <param name="commandTypes">
        /// The types of commands to look for.
        /// </param>
        /// 
        /// <param name="context">
        /// The execution context for this engine instance...
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="PSArgumentException">
        /// If <paramref name="commandName"/> is null or empty.
        /// </exception>
        /// 
        internal CommandSearcher(
            string commandName,
            SearchResolutionOptions options,
            CommandTypes commandTypes,
            ExecutionContext context)
        {
            Diagnostics.Assert(context != null, "caller to verify context is not null");
            Diagnostics.Assert(!string.IsNullOrEmpty(commandName), "caller to verify commandName is valid");

            this.commandName = commandName;
            this._context = context;
            this.commandResolutionOptions = options;
            this.commandTypes = commandTypes;

            // Initialize the enumerators
            this.Reset();
        }
        
        /// <summary>
        /// Gets an instance of a command enumerator
        /// </summary>
        /// 
        /// <returns>
        /// An instance of this class as IEnumerator.
        /// </returns>
        /// 
        IEnumerator<CommandInfo> IEnumerable<CommandInfo>.GetEnumerator()
        {
            return this;
        } // GetEnumerator

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        } // GetEnumerator

        /// <summary>
        /// Moves the enumerator to the next command match. Public for IEnumerable
        /// </summary>
        /// 
        /// <returns>
        /// true if there was another command that matches, false otherwise.
        /// </returns>
        /// 
        public bool MoveNext()
        {
            _currentMatch = null;

            if (currentState == SearchState.SearchingAliases)
            {
                _currentMatch = SearchForAliases();

                // Why don't we check IsVisible on other scoped items?
                if (_currentMatch != null && SessionState.IsVisible(_commandOrigin, _currentMatch))
                {
                    return true;
                }

                // Make sure Current doesn't return an alias that isn't visible
                _currentMatch = null;

                // Advance the state
                currentState = SearchState.SearchingFunctions;
            }

            if (currentState == SearchState.SearchingFunctions)
            {
                _currentMatch = SearchForFunctions();
                // Return the alias info only if it is visible. If not, then skip to the next
                // stage of command resolution...
                if (_currentMatch != null)
                {
                    return true;
                }

                // Advance the state
                currentState = SearchState.SearchingCmdlets;
            }

            if (currentState == SearchState.SearchingCmdlets)
            {
                _currentMatch = SearchForCmdlets();
                if (_currentMatch != null)
                {
                    return true;
                }

                // Advance the state
                currentState = SearchState.SearchingBuiltinScripts;
            }

            if (currentState == SearchState.SearchingBuiltinScripts)
            {
                _currentMatch = SearchForBuiltinScripts();
                if (_currentMatch != null)
                {
                    return true;
                }

                // Advance the state
                currentState = SearchState.StartSearchingForExternalCommands;
            }

            if (currentState == SearchState.StartSearchingForExternalCommands)
            {
                if ((commandTypes & (CommandTypes.Application | CommandTypes.ExternalScript)) == 0)
                {
                    // Since we are not requiring any path lookup in this search, just return false now
                    // because all the remaining searches do path lookup.
                    return false;
                }

                // For security reasons, if the command is coming from outside the runspace and it looks like a path,
                // we want to pre-check that path before doing any probing of the network or drives
                if (_commandOrigin == CommandOrigin.Runspace && commandName.IndexOfAny(Utils.Separators.DirectoryOrDrive) >= 0)
                {
                    bool allowed = false;

                    // Ok - it looks like it might be a path, so we're going to check to see if the command is prefixed
                    // by any of the allowed paths. If so, then we allow the search to proceed...

                    // If either the Applications or Script lists contain just '*' the command is allowed
                    // at this point.
                    if ((_context.EngineSessionState.Applications.Count == 1 &&
                        _context.EngineSessionState.Applications[0].Equals("*", StringComparison.OrdinalIgnoreCase)) ||
                        (_context.EngineSessionState.Scripts.Count == 1 &&
                        _context.EngineSessionState.Scripts[0].Equals("*", StringComparison.OrdinalIgnoreCase)))
                    {
                        allowed = true;
                    }
                    else
                    {
                        // Ok see it it's in the applications list
                        foreach (string path in _context.EngineSessionState.Applications)
                        {
                            if (checkPath(path, commandName))
                            {
                                allowed = true;
                                break;
                            }
                        }

                        // If it wasn't in the applications list, see it's in the script list
                        if (!allowed)
                        {
                            foreach (string path in _context.EngineSessionState.Scripts)
                            {
                                if (checkPath(path, commandName))
                                {
                                    allowed = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!allowed)
                    {
                        return false;
                    }
                }

                // Advance the state

                currentState = SearchState.PowerShellPathResolution;

                _currentMatch = ProcessBuiltinScriptState();

                if (_currentMatch != null)
                {
                    // Set the current state to QualifiedFileSystemPath since
                    // we want to skip the qualified file system path search
                    // in the case where we found a PowerShell qualified path.

                    currentState = SearchState.QualifiedFileSystemPath;
                    return true;
                }
            } // SearchState.Reset

            if (currentState == SearchState.PowerShellPathResolution)
            {
                currentState = SearchState.QualifiedFileSystemPath;

                _currentMatch = ProcessPathResolutionState();

                if (_currentMatch != null)
                {
                    return true;
                }
            } // SearchState.PowerShellPathResolution

            // Search using CommandPathSearch

            if (currentState == SearchState.QualifiedFileSystemPath ||
                    currentState == SearchState.PathSearch)
            {
                _currentMatch = ProcessQualifiedFileSystemState();

                if (_currentMatch != null)
                {
                    return true;
                }
            } // SearchState.QualifiedFileSystemPath || SearchState.PathSearch

            if (currentState == SearchState.PathSearch)
            {
                currentState = SearchState.PowerShellRelativePath;

                _currentMatch = ProcessPathSearchState();

                if (_currentMatch != null)
                {
                    return true;
                }
            }

            return false;
        } // MoveNext

        private CommandInfo SearchForAliases()
        {
            CommandInfo currentMatch = null;

            if (_context.EngineSessionState != null &&
                (commandTypes & CommandTypes.Alias) != 0)
            {
                currentMatch = GetNextAlias();
            }
            return currentMatch;
        }

        private CommandInfo SearchForFunctions()
        {
            CommandInfo currentMatch = null;

            if (_context.EngineSessionState != null &&
                (commandTypes & (CommandTypes.Function | CommandTypes.Filter | CommandTypes.Workflow | CommandTypes.Configuration)) != 0)
            {
                currentMatch = GetNextFunction();
            }

            return currentMatch;
        }

        private CommandInfo SearchForCmdlets()
        {
            CommandInfo currentMatch = null;

            if ((commandTypes & CommandTypes.Cmdlet) != 0)
            {
                currentMatch = GetNextCmdlet();
            }

            return currentMatch;
        }

        private CommandInfo SearchForBuiltinScripts()
        {
            CommandInfo currentMatch = null;

            if ((commandTypes & CommandTypes.Script) != 0)
            {
                currentMatch = GetNextBuiltinScript();
            }

            return currentMatch;
        }

        private CommandInfo ProcessBuiltinScriptState()
        {
            CommandInfo currentMatch = null;

            // Check to see if the path is qualified

            if (_context.EngineSessionState != null &&
                _context.EngineSessionState.ProviderCount > 0 &&
                IsQualifiedPSPath(commandName))
            {
                currentMatch = GetNextFromPath();
            }

            return currentMatch;
        }

        private CommandInfo ProcessPathResolutionState()
        {
            CommandInfo currentMatch = null;

            try
            {
                // Check to see if the path is a file system path that
                // is rooted.  If so that is the next match
                if (Path.IsPathRooted(commandName) &&
                    File.Exists(commandName))
                {
                    try
                    {
                        currentMatch = GetInfoFromPath(commandName);

                    }
                    catch (FileLoadException)
                    {
                    }
                    catch (FormatException)
                    {
                    }
                    catch (MetadataException)
                    {
                    }
                }
            }
            catch (ArgumentException)
            {
                // If the path contains illegal characters that
                // weren't caught by the other APIs, IsPathRooted
                // will throw an exception.
                // For example, looking for a command called
                // `abcdef
                // The `a will be translated into the beep control character
                // which is not a legal file system character, though
                // Path.InvalidPathChars does not contain it as an invalid
                // character.
            }

            return currentMatch;
        }

        private CommandInfo ProcessQualifiedFileSystemState()
        {
            try
            {
                setupPathSearcher();
            }
            catch (ArgumentException)
            {
                currentState = SearchState.NoMoreMatches;
                throw;
            }
            catch (PathTooLongException)
            {
                currentState = SearchState.NoMoreMatches;
                throw;
            }

            CommandInfo currentMatch = null;
            currentState = SearchState.PathSearch;
            if (canDoPathLookup)
            {

                try
                {
                    while (currentMatch == null && this.pathSearcher.MoveNext())
                    {
                        currentMatch = GetInfoFromPath(((IEnumerator<string>)pathSearcher).Current);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The enumerator may throw if there are no more matches
                }
            }
            return currentMatch;
        }

        private CommandInfo ProcessPathSearchState()
        {
            CommandInfo currentMatch = null;
            string path = DoPowerShellRelativePathLookup();

            if (!String.IsNullOrEmpty(path))
            {
                currentMatch = GetInfoFromPath(path);
            }

            return currentMatch;
        }


        /// <summary>
        /// Gets the CommandInfo representing the current command match.
        /// </summary>
        /// <value></value>
        /// 
        /// <exception cref="InvalidOperationException">
        /// The enumerator is positioned before the first element of
        /// the collection or after the last element.
        /// </exception>
        /// 
        CommandInfo IEnumerator<CommandInfo>.Current
        {
            get
            {
                if ((currentState == SearchState.SearchingAliases && _currentMatch == null) ||
                    currentState == SearchState.NoMoreMatches ||
                    _currentMatch == null)
                {
                    throw PSTraceSource.NewInvalidOperationException();
                }

                return _currentMatch;
            }
        } // Current


        object IEnumerator.Current
        {
            get
            {
                return ((IEnumerator<CommandInfo>)this).Current;
            }
        }

        /// <summary>
        /// Required by the IEnumerator generic interface.
        /// Resets the searcher.
        /// </summary>
        public void Dispose()
        {
            if (pathSearcher != null)
            {
                pathSearcher.Dispose();
                pathSearcher = null;
            }

            Reset();
            GC.SuppressFinalize(this);
        }

        #region private members

        /// <summary>
        /// Gets the next command info using the command name as a path
        /// </summary>
        /// 
        /// <returns>
        /// A CommandInfo for the next command if it exists as a path, or null otherwise.
        /// </returns>
        /// 
        private CommandInfo GetNextFromPath()
        {
            CommandInfo result = null;

            do // false loop
            {
                CommandDiscovery.discoveryTracer.WriteLine(
                    "The name appears to be a qualified path: {0}",
                    commandName);

                CommandDiscovery.discoveryTracer.WriteLine(
                    "Trying to resolve the path as an PSPath");

                // Find the match if it is.

                Collection<string> resolvedPaths = new Collection<string>();

                try
                {
                    Provider.CmdletProvider providerInstance;
                    ProviderInfo provider;
                    resolvedPaths =
                        _context.LocationGlobber.GetGlobbedProviderPathsFromMonadPath(commandName, false, out provider, out providerInstance);
                }
                catch (ItemNotFoundException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The path could not be found: {0}",
                        commandName);
                }
                catch (DriveNotFoundException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "A drive could not be found for the path: {0}",
                        commandName);
                }
                catch (ProviderNotFoundException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "A provider could not be found for the path: {0}",
                        commandName);
                }
                catch (InvalidOperationException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The path specified a home directory, but the provider home directory was not set. {0}",
                        commandName);
                }
                catch (ProviderInvocationException providerException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The provider associated with the path '{0}' encountered an error: {1}",
                        commandName,
                        providerException.Message);
                }
                catch (PSNotSupportedException)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The provider associated with the path '{0}' does not implement ContainerCmdletProvider",
                        commandName);
                }

                if (resolvedPaths.Count > 1)
                {
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The path resolved to more than one result so this path cannot be used.");
                    break;
                }

                // If the path was resolved, and it exists
                if (resolvedPaths.Count == 1 &&
                    File.Exists(resolvedPaths[0]))
                {
                    string path = resolvedPaths[0];

                    CommandDiscovery.discoveryTracer.WriteLine(
                        "Path resolved to: {0}",
                        path);

                    result = GetInfoFromPath(path);
                }
            } while (false);

            return result;
        }

        private static bool checkPath(string path, string commandName)
        {
            return path.StartsWith(commandName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the appropriate CommandInfo instance given the specified path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to create the CommandInfo for.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the appropriate CommandInfo derivative given the specified path.
        /// </returns>
        /// 
        /// <exception cref="FileLoadException">
        /// The <paramref name="path"/> refers to a cmdlet, or cmdletprovider
        /// and it could not be loaded as an XML document.
        /// </exception>
        /// 
        /// <exception cref="FormatException">
        /// The <paramref name="path"/> refers to a cmdlet, or cmdletprovider
        /// that does not adhere to the appropriate file format for its extension.
        /// </exception>
        /// 
        /// <exception cref="MetadataException">
        /// If <paramref name="path"/> refers to a cmdlet file that 
        /// contains invalid metadata.
        /// </exception>
        /// 
        private CommandInfo GetInfoFromPath(string path)
        {
            CommandInfo result = null;

            do // false loop
            {
                if (!Utils.NativeFileExists(path))
                {
                    CommandDiscovery.discoveryTracer.TraceError("The path does not exist: {0}", path);
                    break;
                }

                // Now create the appropriate CommandInfo using the extension
                string extension = null;

                try
                {
                    extension = Path.GetExtension(path);
                }
                catch (ArgumentException)
                {
                    // If the path contains illegal characters that
                    // weren't caught by the other APIs, GetExtension
                    // will throw an exception.
                    // For example, looking for a command called
                    // `abcdef
                    // The `a will be translated into the beep control character
                    // which is not a legal file system character.
                }

                if (extension == null)
                {
                    result = null;
                    break;
                }

                if (String.Equals(extension, StringLiterals.PowerShellScriptFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if ((this.commandTypes & CommandTypes.ExternalScript) != 0)
                    {
                        string scriptName = Path.GetFileName(path);

                        CommandDiscovery.discoveryTracer.WriteLine(
                            "Command Found: path ({0}) is a script with name: {1}",
                            path,
                            scriptName);

                        // The path is to a PowerShell script

                        result = new ExternalScriptInfo(scriptName, path, _context);
                        break;
                    }
                    break;
                }


                if ((this.commandTypes & CommandTypes.Application) != 0)
                {
                    // Anything else is treated like an application

                    string appName = Path.GetFileName(path);

                    CommandDiscovery.discoveryTracer.WriteLine(
                        "Command Found: path ({0}) is an application with name: {1}",
                        path,
                        appName);

                    result = new ApplicationInfo(appName, path, _context);
                    break;
                }

            } while (false);

            // Verify that this script is not untrusted, if we aren't constrained.
            if(ShouldSkipCommandResolutionForConstrainedLanguage(result, this._context))
            {
                result = null;
            }

            return result;
        } // GetNextFromPath

        /// <summary>
        /// Gets the next matching alias
        /// </summary>
        /// 
        /// <returns>
        /// A CommandInfo representing the next matching alias if found, otherwise null.
        /// </returns>
        /// 
        private CommandInfo GetNextAlias()
        {
            CommandInfo result = null;

            if ((commandResolutionOptions & SearchResolutionOptions.ResolveAliasPatterns) != 0)
            {
                if (matchingAlias == null)
                {
                    // Generate the enumerator of matching alias names

                    Collection<AliasInfo> matchingAliases = new Collection<AliasInfo>();

                    WildcardPattern aliasMatcher =
                        WildcardPattern.Get(
                            commandName,
                            WildcardOptions.IgnoreCase);

                    foreach (KeyValuePair<string, AliasInfo> aliasEntry in _context.EngineSessionState.GetAliasTable())
                    {
                        if (aliasMatcher.IsMatch(aliasEntry.Key))
                        {
                            matchingAliases.Add(aliasEntry.Value);
                        }
                    }

                    // Process alias from modules
                    AliasInfo c = GetAliasFromModules(commandName);
                    if (c != null)
                    {
                        matchingAliases.Add(c);
                    }

                    matchingAlias = matchingAliases.GetEnumerator();
                }

                if (!matchingAlias.MoveNext())
                {
                    // Advance the state
                    currentState = SearchState.SearchingFunctions;

                    matchingAlias = null;
                }
                else
                {
                    result = matchingAlias.Current;
                }
            }
            else
            {
                // Advance the state
                currentState = SearchState.SearchingFunctions;

                result = _context.EngineSessionState.GetAlias(commandName) ?? GetAliasFromModules(commandName);
            }

            // Verify that this alias was not created by an untrusted constrained language,
            // if we aren't constrained.
            if(ShouldSkipCommandResolutionForConstrainedLanguage(result, this._context))
            {
                result = null;
            }

            if (result != null)
            {
                CommandDiscovery.discoveryTracer.WriteLine(
                    "Alias found: {0}  {1}",
                    result.Name,
                    result.Definition);
            }
            return result;
        } // GetNextAlias

        /// <summary>
        /// Gets the next matching function
        /// </summary>
        /// 
        /// <returns>
        /// A CommandInfo representing the next matching function if found, otherwise null.
        /// </returns>
        /// 
        private CommandInfo GetNextFunction()
        {
            CommandInfo result = null;

            if ((commandResolutionOptions & SearchResolutionOptions.ResolveFunctionPatterns) != 0)
            {
                if (matchingFunctionEnumerator == null)
                {
                    Collection<CommandInfo> matchingFunction = new Collection<CommandInfo>();

                    // Generate the enumerator of matching function names
                    WildcardPattern functionMatcher =
                        WildcardPattern.Get(                            
                            commandName,
                            WildcardOptions.IgnoreCase);                    

                    foreach (DictionaryEntry functionEntry in _context.EngineSessionState.GetFunctionTable())
                    {
                        if (functionMatcher.IsMatch((string)functionEntry.Key))
                        {
                            matchingFunction.Add((CommandInfo)functionEntry.Value);
                        }
                    }

                    // Process functions from modules
                    CommandInfo c = GetFunctionFromModules(commandName);
                    if (c != null)
                    {
                        matchingFunction.Add(c);
                    }

                    matchingFunctionEnumerator = matchingFunction.GetEnumerator();
                }

                if (!matchingFunctionEnumerator.MoveNext())
                {
                    // Advance the state
                    currentState = SearchState.SearchingCmdlets;

                    matchingFunctionEnumerator = null;
                }
                else
                {
                    result = matchingFunctionEnumerator.Current;
                }
            }
            else
            {
                // Advance the state
                currentState = SearchState.SearchingCmdlets;

                result = GetFunction(commandName);
            }

            // Verify that this function was not created by an untrusted constrained language,
            // if we aren't constrained.
            if(ShouldSkipCommandResolutionForConstrainedLanguage(result, this._context))
            {
                result = null;
            }

            return result;
        }

        // Don't return commands to the user if that might result in:
        //     - Trusted commands calling untrusted functions that the user has overridden
        //     - Debug prompts calling internal functions that are likely to have code injection
        private bool ShouldSkipCommandResolutionForConstrainedLanguage(CommandInfo result, ExecutionContext executionContext)
        {
            if(result == null)
            {
                return false;
            }

            // Don't return untrusted commands to trusted functions
            if((result.DefiningLanguageMode == PSLanguageMode.ConstrainedLanguage) &&
                (executionContext.LanguageMode == PSLanguageMode.FullLanguage))
            {
                return true;
            }

            // Don't allow invocation of trusted functions from debug breakpoints.
            // They were probably defined within a trusted script, and could be
            // susceptible to injection attacks. However, we do allow execution
            // of functions defined in the global scope (i.e.: "more",) as those
            // are intended to be exposed explicitly to users.
            if((result is FunctionInfo) &&
                (executionContext.LanguageMode == PSLanguageMode.ConstrainedLanguage) &&
                (result.DefiningLanguageMode == PSLanguageMode.FullLanguage) &&
                (executionContext.Debugger != null) &&
                (executionContext.Debugger.InBreakpoint) &&
                (! (executionContext.TopLevelSessionState.GetFunctionTableAtScope("GLOBAL").ContainsKey(result.Name))))
            {
                return true;
            }

            return false;
        }

        private AliasInfo GetAliasFromModules(string command)
        {
            AliasInfo result = null;
            
            if (command.IndexOf('\\') > 0)
            {
                // See if it's a module qualified alias...
                PSSnapinQualifiedName qualifiedName = PSSnapinQualifiedName.GetInstance(command);
                if (qualifiedName != null && !string.IsNullOrEmpty(qualifiedName.PSSnapInName))
                {
                    PSModuleInfo module = GetImportedModuleByName(qualifiedName.PSSnapInName);

                    if (module != null)
                    {
                        module.ExportedAliases.TryGetValue(qualifiedName.ShortName, out result);
                    }
                }
            }
            return result;
        }

        private CommandInfo GetFunctionFromModules(string command)
        {
            FunctionInfo result = null;

            if (command.IndexOf('\\') > 0)
            {
                // See if it's a module qualified function call...
                PSSnapinQualifiedName qualifiedName = PSSnapinQualifiedName.GetInstance(command);
                if (qualifiedName != null && !string.IsNullOrEmpty(qualifiedName.PSSnapInName))
                {
                    PSModuleInfo module = GetImportedModuleByName(qualifiedName.PSSnapInName);
                    
                    if (module != null)
                    {
                        module.ExportedFunctions.TryGetValue(qualifiedName.ShortName, out result);
                    }
                }
            }
            return result;
        }

        private PSModuleInfo GetImportedModuleByName(string moduleName)
        {
            PSModuleInfo module = null;
            List<PSModuleInfo> modules = _context.Modules.GetModules(new string[] { moduleName }, false);

            if (modules != null && modules.Count > 0)
            {
                foreach (PSModuleInfo m in modules)
                {
                    if (_context.previousModuleImported.ContainsKey(m.Name) && ((string)_context.previousModuleImported[m.Name] == m.Path))
                    {
                        module = m;
                        break;
                    }
                }
                if (module == null)
                {
                    module = modules[0];
                }
            }

            return module;
        }

        /// <summary>
        /// Gets the FunctionInfo or FilterInfo for the specified function name.
        /// </summary>
        /// 
        /// <param name="function">
        /// The name of the function/filter to retrieve.
        /// </param>
        /// 
        /// <returns>
        /// A FunctionInfo if the function name exists and is a function, a FilterInfo if
        /// the filter name exists and is a filter, or null otherwise.
        /// </returns>
        /// 
        private CommandInfo GetFunction(string function)
        {
            CommandInfo result = _context.EngineSessionState.GetFunction(function);

            if (result != null)
            {
                if (result is FilterInfo)
                {
                    CommandDiscovery.discoveryTracer.WriteLine(
                        "Filter found: {0}",
                        function);
                }
                else if (result is ConfigurationInfo)
                {
                    CommandDiscovery.discoveryTracer.WriteLine(
                        "Configuration found: {0}",
                        function);
                }
                else
                {
                    CommandDiscovery.discoveryTracer.WriteLine(
                        "Function found: {0}  {1}",
                        function);
                }
            }
            else 
            {
                result = GetFunctionFromModules(function);
            }
            return result;
        } // GetFunction

        /// <summary>
        /// Gets the next cmdlet from the collection of matching cmdlets.
        /// If the collection doesn't exist yet it is created and the
        /// enumerator is moved to the first item in the collection.
        /// </summary>
        /// 
        /// <returns>
        /// A CmdletInfo for the next matching Cmdlet or null if there are
        /// no more matches.
        /// </returns>
        /// 
        private CmdletInfo GetNextCmdlet()
        {
            CmdletInfo result = null;

            if (matchingCmdlet == null)
            {
                if ((commandResolutionOptions & SearchResolutionOptions.CommandNameIsPattern) != 0)
                {
                    Collection<CmdletInfo> matchingCmdletInfo = new Collection<CmdletInfo>();

                    PSSnapinQualifiedName PSSnapinQualifiedCommandName =
                        PSSnapinQualifiedName.GetInstance(commandName);

                    if (PSSnapinQualifiedCommandName == null)
                    {
                        return result;
                    }

                    WildcardPattern cmdletMatcher =
                        WildcardPattern.Get(
                            PSSnapinQualifiedCommandName.ShortName,
                            WildcardOptions.IgnoreCase);

                    SessionStateInternal ss = _context.EngineSessionState;

                    foreach (List<CmdletInfo> cmdletList in ss.GetCmdletTable().Values)
                    {
                        foreach (CmdletInfo cmdlet in cmdletList)
                        {
                            if (cmdletMatcher.IsMatch(cmdlet.Name))
                            {
                                if (string.IsNullOrEmpty(PSSnapinQualifiedCommandName.PSSnapInName) ||
                                    (PSSnapinQualifiedCommandName.PSSnapInName.Equals(
                                        cmdlet.ModuleName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    // If PSSnapin is specified, make sure they match
                                    matchingCmdletInfo.Add(cmdlet);
                                }
                            }
                        }
                    }

                    matchingCmdlet = matchingCmdletInfo.GetEnumerator();
                }
                else
                {
                    matchingCmdlet = _context.CommandDiscovery.GetCmdletInfo(commandName,
                        (commandResolutionOptions & SearchResolutionOptions.SearchAllScopes) != 0);
                }
            }

            if (!matchingCmdlet.MoveNext())
            {
                // Advance the state
                currentState = SearchState.SearchingBuiltinScripts;

                matchingCmdlet = null;
            }
            else
            {
                result = matchingCmdlet.Current;
            }

            return traceResult(result);
        }
        private IEnumerator<CmdletInfo> matchingCmdlet;

        private static CmdletInfo traceResult(CmdletInfo result)
        {
            if (result != null)
            {
                CommandDiscovery.discoveryTracer.WriteLine(
                    "Cmdlet found: {0}  {1}",
                    result.Name,
                    result.ImplementingType);
            }
            return result;
        }

        /// <summary>
        /// Gets the next builtin script from the collection of matching scripts.
        /// If the collection doesn't exist yet it is created and the
        /// enumerator is moved to the first item in the collection.
        /// </summary>
        /// 
        /// <returns>
        /// A ScriptInfo for the next matching script or null if there are
        /// no more matches.
        /// </returns>
        /// 
        private ScriptInfo GetNextBuiltinScript()
        {
            ScriptInfo result = null;

            if ((commandResolutionOptions & SearchResolutionOptions.CommandNameIsPattern) != 0)
            {
                if (matchingScript == null)
                {
                    // Generate the enumerator of matching script names

                    Collection<string> matchingScripts =
                        new Collection<string>();

                    WildcardPattern scriptMatcher =
                        WildcardPattern.Get(
                            commandName,
                            WildcardOptions.IgnoreCase);

                    WildcardPattern scriptExtensionMatcher =
                        WildcardPattern.Get(
                            commandName + StringLiterals.PowerShellScriptFileExtension,
                            WildcardOptions.IgnoreCase);

                    // Get the script cache enumerator. This acquires the cache lock,
                    // so be sure to dispose.

                    foreach (string scriptName in _context.CommandDiscovery.ScriptCache.Keys)
                    {
                        if (scriptMatcher.IsMatch(scriptName) ||
                            scriptExtensionMatcher.IsMatch(scriptName))
                        {
                            matchingScripts.Add(scriptName);
                        }
                    }
                    matchingScript = matchingScripts.GetEnumerator();
                }

                if (!matchingScript.MoveNext())
                {
                    // Advance the state
                    currentState = SearchState.StartSearchingForExternalCommands;

                    matchingScript = null;
                }
                else
                {
                    result = _context.CommandDiscovery.GetScriptInfo(matchingScript.Current);
                }
            }
            else
            {
                // Advance the state
                currentState = SearchState.StartSearchingForExternalCommands;

                result = _context.CommandDiscovery.GetScriptInfo(commandName) ??
                         _context.CommandDiscovery.GetScriptInfo(commandName + StringLiterals.PowerShellScriptFileExtension);
            }

            if (result != null)
            {
                CommandDiscovery.discoveryTracer.WriteLine(
                    "Script found: {0}",
                    result.Name);
            }
            return result;
        }
        private IEnumerator<string> matchingScript;


        private string DoPowerShellRelativePathLookup()
        {
            string result = null;

            if (_context.EngineSessionState != null &&
                _context.EngineSessionState.ProviderCount > 0)
            {
                // NTRAID#Windows OS Bugs-1009294-2004/02/04-JeffJon
                // This is really slow.  Maybe since we are only allowing FS paths right
                // now we should use the file system APIs to verify the existence of the file.

                // Since the path to the command was not found using the PATH variable,
                // maybe it is relative to the current location. Try resolving the
                // path.
                // Relative Path:       ".\command.exe"
                // Home Path:           "~\command.exe"
                // Drive Relative Path: "\Users\User\AppData\Local\Temp\command.exe"
                if (commandName[0] == '.' || commandName[0] == '~' || commandName[0] == '\\')
                {
                    using (CommandDiscovery.discoveryTracer.TraceScope(
                        "{0} appears to be a relative path. Trying to resolve relative path",
                        commandName))
                    {
                        result = ResolvePSPath(commandName);
                    }
                }
            }
            return result;
        } // DoPowerShellRelativePathLookup

        /// <summary>
        /// Resolves the given path as an PSPath and ensures that it was resolved
        /// by the FileSystemProvider
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to resolve.
        /// </param>
        /// 
        /// <returns>
        /// The path that was resolved. Null if the path couldn't be resolved or was
        /// not resolved by the FileSystemProvider.
        /// </returns>
        /// 
        private string ResolvePSPath(string path)
        {
            string result = null;

            try
            {
                ProviderInfo provider = null;
                string resolvedPath = null;
                if (WildcardPattern.ContainsWildcardCharacters(path))
                {
                    // Let PowerShell resolve relative path with wildcards.
                    Provider.CmdletProvider providerInstance;
                    Collection<string> resolvedPaths = _context.LocationGlobber.GetGlobbedProviderPathsFromMonadPath(
                        path,
                        false,
                        out provider,
                        out providerInstance);

                    if (resolvedPaths.Count == 0)
                    {
                        resolvedPath = null;

                        CommandDiscovery.discoveryTracer.TraceError(
                           "The relative path with wildcard did not resolve to valid path. {0}",
                           path);
                    }
                    else if (resolvedPaths.Count > 1)
                    {
                        resolvedPath = null;

                        CommandDiscovery.discoveryTracer.TraceError(
                        "The relative path with wildcard resolved to mutiple paths. {0}",
                        path);
                    }
                    else
                    {
                        resolvedPath = resolvedPaths[0];
                    }
                }

                // Revert to previous path resolver if wildcards produces no results.
                if ((resolvedPath == null) || (provider == null))
                {
                    resolvedPath = _context.LocationGlobber.GetProviderPath(path, out provider);
                }

                // Verify the path was resolved to a file system path
                if (provider.NameEquals(_context.ProviderNames.FileSystem))
                {
                    result = resolvedPath;

                    CommandDiscovery.discoveryTracer.WriteLine(
                        "The relative path was resolved to: {0}",
                        result);
                }
                else
                {
                    // The path was not to the file system
                    CommandDiscovery.discoveryTracer.TraceError(
                        "The relative path was not a file system path. {0}",
                        path);
                }
            }
            catch (InvalidOperationException)
            {
                CommandDiscovery.discoveryTracer.TraceError(
                    "The home path was not specified for the provider. {0}",
                    path);
            }
            catch (ProviderInvocationException providerInvocationException)
            {
                CommandDiscovery.discoveryTracer.TraceError(
                    "While resolving the path, \"{0}\", an error was encountered by the provider: {1}",
                    path,
                    providerInvocationException.Message);
            }
            catch (ItemNotFoundException)
            {
                CommandDiscovery.discoveryTracer.TraceError(
                    "The path does not exist: {0}",
                    path);
            }
            catch (DriveNotFoundException driveNotFound)
            {
                CommandDiscovery.discoveryTracer.TraceError(
                    "The drive does not exist: {0}",
                    driveNotFound.ItemName);
            }

            return result;
        } // ResolvePSPath

        /// <summary>
        /// Creates a collection of patterns used to find the command
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the command to search for.
        /// </param>
        /// <param name="commandDiscovery">get names for command discovery</param>
        /// <returns>
        /// A collection of the patterns used to find the command.
        /// The patterns are as follows:
        ///     1. [commandName].cmdlet
        ///     2. [commandName].ps1
        ///     3..x 
        ///         foreach (extension in PATHEXT)
        ///             [commandName].[extension]
        ///     x+1. [commandName]
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> contains one or more of the 
        /// invalid characters defined in InvalidPathChars.
        /// </exception>
        internal Collection<string> ConstructSearchPatternsFromName(string name, bool commandDiscovery = false)
        {
            Dbg.Assert(
                !String.IsNullOrEmpty(name),
                "Caller should verify name");

            Collection<string> result = new Collection<string>();

            // First check to see if the commandName has an extension, if so
            // look for that first

            bool commandNameAddedFirst = false;

            if (!String.IsNullOrEmpty(Path.GetExtension(name)))
            {
                result.Add(name);
                commandNameAddedFirst = true;
            }

            // Add the extensions for script, module and data files in that order...
            if ((commandTypes & CommandTypes.ExternalScript) != 0)
            {
                result.Add(name + StringLiterals.PowerShellScriptFileExtension);
                if (!commandDiscovery)
                {
                    // psd1 and psm1 are not executable, so don't add them
                    result.Add(name + StringLiterals.PowerShellModuleFileExtension);
                    result.Add(name + StringLiterals.PowerShellDataFileExtension);
                }
            }

            if ((commandTypes & CommandTypes.Application) != 0)
            {
                // Now add each extension from the PATHEXT environment variable

                foreach (string extension in CommandDiscovery.PathExtensions)
                {
                    result.Add(name + extension);
                }
            }

            // Now add the commandName by itself if it wasn't added as the first
            // pattern

            if (!commandNameAddedFirst)
            {
                result.Add(name);
            }
            return result;
        } // ConstructSearchPatternsFromName

        /// <summary>
        /// Determines if the given command name is a qualified PowerShell path.
        /// </summary>
        /// 
        /// <param name="commandName">
        /// The name of the command.
        /// </param>
        /// 
        /// <returns>
        /// True if the command name is either a provider-qualified or PowerShell drive-qualified
        /// path. False otherwise.
        /// </returns>
        /// 
        private static bool IsQualifiedPSPath(string commandName)
        {
            Dbg.Assert(
                !String.IsNullOrEmpty(commandName),
                "The caller should have verified the commandName");

            bool result =
                LocationGlobber.IsAbsolutePath(commandName) ||
                LocationGlobber.IsProviderQualifiedPath(commandName) ||
                LocationGlobber.IsHomePath(commandName) ||
                LocationGlobber.IsProviderDirectPath(commandName);

            return result;
        } // IsQualifiedPSPath

        private enum CanDoPathLookupResult
        {
            Yes,
            PathIsRooted,
            WildcardCharacters,
            DirectorySeparator,
            IllegalCharacters
        }

        /// <summary>
        /// Determines if the command name has any path special
        /// characters which would require resolution. If so,
        /// path lookup will not succeed.
        /// </summary>
        /// 
        /// <param name="possiblePath">
        /// The command name (or possible path) to look for the special characters.
        /// </param>
        /// 
        /// <returns>
        /// True if the command name does not contain any special
        /// characters.  False otherwise.
        /// </returns>
        /// 
        private static CanDoPathLookupResult CanDoPathLookup(string possiblePath)
        {
            CanDoPathLookupResult result = CanDoPathLookupResult.Yes;

            do // false loop
            {
                // If the command name contains any wildcard characters
                // we can't do the path lookup

                if (WildcardPattern.ContainsWildcardCharacters(possiblePath))
                {
                    result = CanDoPathLookupResult.WildcardCharacters;
                    break;
                }

                try
                {
                    if (Path.IsPathRooted(possiblePath))
                    {
                        result = CanDoPathLookupResult.PathIsRooted;
                        break;
                    }
                }
                catch (ArgumentException)
                {
                    result = CanDoPathLookupResult.IllegalCharacters;
                    break;
                }

                // If the command contains any path separators, we can't
                // do the path lookup
                if (possiblePath.IndexOfAny(Utils.Separators.Directory) != -1)
                {
                    result = CanDoPathLookupResult.DirectorySeparator;
                    break;
                }

                // If the command contains any invalid path characters, we can't
                // do the path lookup

                if (possiblePath.IndexOfAny(Path.GetInvalidPathChars()) != -1)
                {
                    result = CanDoPathLookupResult.IllegalCharacters;
                    break;
                }
            } while (false);

            return result;
        } // CanDoPathLookup


        /// <summary>
        /// The command name to search for
        /// </summary>
        private string commandName;

        /// <summary>
        /// Determines which command types will be globbed.
        /// </summary>
        private SearchResolutionOptions commandResolutionOptions;

        /// <summary>
        /// Determines which types of commands to look for.
        /// </summary>
        private CommandTypes commandTypes = CommandTypes.All;

        /// <summary>
        /// The enumerator that uses the Path to
        /// search for commands.
        /// </summary>
        private CommandPathSearch pathSearcher;

        /// <summary>
        /// Thge execution context instance for the current engine...
        /// </summary>
        private ExecutionContext _context;

        /// <summary>
        /// A routine to initialize the path searcher...
        /// </summary>
        /// 
        /// <exception cref="ArgumentException">
        /// If the commandName used to construct this object
        /// contains one or more of the invalid characters defined
        /// in InvalidPathChars.
        /// </exception>
        /// 
        private void setupPathSearcher()
        {
            // If it's already set up, just return...
            if (pathSearcher != null)
            {
                return;
            }

            // We are never going to look for non-executable commands in CommandSearcher. 
            // Even though file types like .DOC, .LOG,.TXT, etc. can be opened / invoked, users think of these as files, not applications. 
            // So I don't think we should show applications with the additional extensions at all. 
            // Applications should only include files whose extensions are in the PATHEXT list and these would only be returned with the All parameter. 
            
            if ((this.commandResolutionOptions & SearchResolutionOptions.CommandNameIsPattern) != 0)
            {
                canDoPathLookup = true;
                canDoPathLookupResult = CanDoPathLookupResult.Yes;

                pathSearcher =
                    new CommandPathSearch(
                        commandName,
                        _context.CommandDiscovery.GetLookupDirectoryPaths(),
                        _context,
                        acceptableCommandNames: null);
            }
            else
            {
                canDoPathLookupResult = CanDoPathLookup(commandName);
                if (canDoPathLookupResult == CanDoPathLookupResult.Yes)
                {
                    canDoPathLookup = true;
                    commandName = commandName.TrimEnd(Utils.Separators.PathSearchTrimEnd);

                    pathSearcher =
                        new CommandPathSearch(
                            commandName,
                            _context.CommandDiscovery.GetLookupDirectoryPaths(),
                            _context,
                            ConstructSearchPatternsFromName(commandName, commandDiscovery: true));
                }
                else if (canDoPathLookupResult == CanDoPathLookupResult.PathIsRooted)
                {
                    canDoPathLookup = true;

                    string directory = Path.GetDirectoryName(commandName);
                    var directoryCollection = new [] { directory };

                    CommandDiscovery.discoveryTracer.WriteLine(
                        "The path is rooted, so only doing the lookup in the specified directory: {0}",
                        directory);

                    string fileName = Path.GetFileName(commandName);

                    if (!String.IsNullOrEmpty(fileName))
                    {
                        fileName = fileName.TrimEnd(Utils.Separators.PathSearchTrimEnd);
                        pathSearcher =
                            new CommandPathSearch(
                                fileName,
                                directoryCollection,
                                _context,
                                ConstructSearchPatternsFromName(fileName, commandDiscovery: true));
                    }
                    else
                    {
                        canDoPathLookup = false;
                    }
                }
                else if (canDoPathLookupResult == CanDoPathLookupResult.DirectorySeparator)
                {
                    canDoPathLookup = true;

                    // We must try to resolve the path as an PSPath or else we can't do
                    // path lookup for relative paths.

                    string directory = Path.GetDirectoryName(commandName);
                    directory = ResolvePSPath(directory);

                    CommandDiscovery.discoveryTracer.WriteLine(
                        "The path is relative, so only doing the lookup in the specified directory: {0}",
                        directory);

                    if (directory == null)
                    {
                        canDoPathLookup = false;
                    }
                    else
                    {
                        var directoryCollection = new[] { directory };

                        string fileName = Path.GetFileName(commandName);

                        if (!String.IsNullOrEmpty(fileName))
                        {
                            fileName = fileName.TrimEnd(Utils.Separators.PathSearchTrimEnd);
                            pathSearcher =
                                new CommandPathSearch(
                                    fileName,
                                    directoryCollection,
                                    _context,
                                    ConstructSearchPatternsFromName(fileName, commandDiscovery: true));
                        }
                        else
                        {
                            canDoPathLookup = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resets the enumerator to before the first command match, public for IEnumerable
        /// </summary>
        public void Reset()
        {
            // If this is a command coming from outside the runspace and there are no
            // permitted scripts or applications,
            // remove them from the set of things to search for...
            if (_commandOrigin == CommandOrigin.Runspace)
            {
                if (_context.EngineSessionState.Applications.Count == 0)
                    this.commandTypes &= ~CommandTypes.Application;
                if (_context.EngineSessionState.Scripts.Count == 0)
                    this.commandTypes &= ~CommandTypes.ExternalScript;
            }

            if (pathSearcher != null)
            {
                pathSearcher.Reset();
            }
            _currentMatch = null;
            currentState = SearchState.SearchingAliases;
            matchingAlias = null;
            matchingCmdlet = null;
            matchingScript = null;
        } // Reset

        internal CommandOrigin CommandOrigin
        {
            get { return _commandOrigin; }
            set { _commandOrigin = value; }
        }
        CommandOrigin _commandOrigin = CommandOrigin.Internal;

        /// <summary>
        /// An enumerator of the matching aliases
        /// </summary>
        private IEnumerator<AliasInfo> matchingAlias;

        /// <summary>
        /// An enumerator of the matching functions
        /// </summary>
        private IEnumerator<CommandInfo> matchingFunctionEnumerator;

        /// <summary>
        /// The CommandInfo that references the command that matches the pattern.
        /// </summary>
        private CommandInfo _currentMatch;

        private bool canDoPathLookup;
        private CanDoPathLookupResult canDoPathLookupResult = CanDoPathLookupResult.Yes;

        /// <summary>
        /// The current state of the enumerator
        /// </summary>
        private SearchState currentState = SearchState.SearchingAliases;

        enum SearchState
        {
            // the searcher has been reset or has not been advanced since being created.
            SearchingAliases,

            // the searcher has finished alias resolution and is now searching for functions.
            SearchingFunctions,

            // the searcher has finished function resolution and is now searching for cmdlets
            SearchingCmdlets,

            // the search has finished cmdlet resolution and is now searching for builtin scripts
            SearchingBuiltinScripts,

            // the search has finished builtin script resolution and is now searching for external commands
            StartSearchingForExternalCommands,

            // the searcher has moved to 
            PowerShellPathResolution,

            // the searcher has moved to a qualified file system path
            QualifiedFileSystemPath,

            // the searcher has moved to using a CommandPathSearch object
            // for resolution
            PathSearch,

            // the searcher has moved to using a CommandPathSearch object
            // with get prepended to the command name for resolution
            GetPathSearch,

            // the searcher has moved to resolving the command as a
            // relative PowerShell path
            PowerShellRelativePath,

            // No more matches can be found
            NoMoreMatches,
        } // SearchState

        #endregion private members

    } // CommandSearcher

    /// <summary>
    /// Determines which types of commands should be globbed using the specified
    /// pattern. Any flag that is not specified will only match if exact.
    /// </summary>
    [Flags]
    internal enum SearchResolutionOptions
    {
        None = 0x0,
        ResolveAliasPatterns = 0x01,
        ResolveFunctionPatterns = 0x02,
        CommandNameIsPattern = 0x04,
        SearchAllScopes = 0x08,
    }
}
