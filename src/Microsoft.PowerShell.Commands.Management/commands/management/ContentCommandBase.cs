/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Provider;
using Dbg = System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// The base class for the */content commands
    /// </summary>
    public class ContentCommandBase : CoreCommandWithCredentialsBase, IDisposable
    {
        #region Parameters
        
        /// <summary>
        /// Gets or sets the path parameter to the command
        /// </summary>
        [Parameter(Position=0, ParameterSetName = "Path",
                   Mandatory =  true, ValueFromPipelineByPropertyName = true)]
        public string[] Path
        {
            get
            {
                return paths;
            } // get

            set
            {
                paths = value;
            } // set
        } // Path

        /// <summary>
        /// Gets or sets the literal path parameter to the command
        /// </summary>
        [Parameter(ParameterSetName = "LiteralPath",
                   Mandatory = true, ValueFromPipeline=false, ValueFromPipelineByPropertyName = true)]
        [Alias("PSPath")]
        public string[] LiteralPath
        {
            get
            {
                return paths;
            } // get

            set
            {
                base.SuppressWildcardExpansion = true;
                paths = value;
            } // set
        } // LiteralPath

        /// <summary>
        /// Gets or sets the filter property
        /// </summary>
        [Parameter]
        public override string Filter
        {
            get
            {
                return base.Filter;
            } // get

            set
            {
                base.Filter = value;
            } // set
        } // Filter
        
        /// <summary>
        /// Gets or sets the include property
        /// </summary>
        [Parameter]
        public override string[] Include
        {
            get
            {
                return base.Include;
            } // get

            set
            {
                base.Include = value;
            } // set
        } // Include

        /// <summary>
        /// Gets or sets the exclude property
        /// </summary>
        [Parameter]
        public override string[] Exclude
        {
            get
            {
                return base.Exclude;
            } // get

            set
            {
                base.Exclude = value;
            } // set
        } // Exclude

        /// <summary>
        /// Gets or sets the force property
        /// </summary>
        ///
        /// <remarks>
        /// Gives the provider guidance on how vigorous it should be about performing
        /// the operation. If true, the provider should do everything possible to perform
        /// the operation. If false, the provider should attempt the operation but allow
        /// even simple errors to terminate the operation.
        /// For example, if the user tries to copy a file to a path that already exists and
        /// the destination is read-only, if force is true, the provider should copy over
        /// the existing read-only file. If force is false, the provider should write an error.
        /// </remarks>
        ///
        [Parameter]
        public override SwitchParameter Force
        {
            get
            {
                return base.Force;
            }
            set
            {
                base.Force = value;
            }
        } // Force


        #endregion Parameters

        #region parameter data
        
        /// <summary>
        /// The path to the item to ping
        /// </summary>
        private string[] paths;

        #endregion parameter data

        #region protected members

        /// <summary>
        /// An array of content holder objects that contain the path information
        /// and content readers/writers for the item represented by the path information.
        /// </summary>
        /// 
        internal List<ContentHolder> contentStreams = new List<ContentHolder>();

        /// <summary>
        /// Wraps the content into a PSObject and adds context information as notes
        /// </summary>
        /// 
        /// <param name="content">
        /// The content being written out.
        /// </param>
        /// 
        /// <param name="readCount">
        /// The number of blocks that have been read so far.
        /// </param>
        /// 
        /// <param name="pathInfo">
        /// The context the content was retrieved from.
        /// </param>
        /// 
        /// <param name="context">
        /// The context the command is being run under.
        /// </param>
        /// 
        internal void WriteContentObject(object content, long readCount, PathInfo pathInfo, CmdletProviderContext context)
        {
            Dbg.Diagnostics.Assert(
                content != null,
                "The caller should verify the content.");

            Dbg.Diagnostics.Assert(
                pathInfo != null,
                "The caller should verify the pathInfo.");

            Dbg.Diagnostics.Assert(
                context != null,
                "The caller should verify the context.");

            PSObject result = PSObject.AsPSObject(content);

            Dbg.Diagnostics.Assert(
                result != null,
                "A PSObject should always be constructed.");

            // Use the cached notes if the cache exists and the path is still the same
            PSNoteProperty note;

            if (currentContentItem != null &&
                ((currentContentItem.PathInfo == pathInfo) ||
                 ( 
                    String.Compare(
                        pathInfo.Path,
                        currentContentItem.PathInfo.Path,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    )
                )
            {
                result = currentContentItem.AttachNotes(result);
            }
            else
            {
                // Generate a new cache item and cache the notes

                currentContentItem = new ContentPathsCache(pathInfo);

                // Construct a provider qualified path as the Path note
                string psPath = pathInfo.Path;
                note = new PSNoteProperty("PSPath", psPath);
                result.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSPath", psPath);
                currentContentItem.PSPath = psPath;

                try
                {
                    // Now get the parent path and child name

                    string parentPath = null;

                    if (pathInfo.Drive != null)
                    {
                        parentPath = SessionState.Path.ParseParent(pathInfo.Path, pathInfo.Drive.Root, context);
                    }
                    else
                    {
                        parentPath = SessionState.Path.ParseParent(pathInfo.Path, String.Empty, context);
                    }
                    note = new PSNoteProperty("PSParentPath", parentPath);
                    result.Properties.Add(note, true);
                    tracer.WriteLine("Attaching {0} = {1}", "PSParentPath", parentPath);
                    currentContentItem.ParentPath = parentPath;

                    // Get the child name

                    string childName = SessionState.Path.ParseChildName(pathInfo.Path, context);
                    note = new PSNoteProperty("PSChildName", childName);
                    result.Properties.Add(note, true);
                    tracer.WriteLine("Attaching {0} = {1}", "PSChildName", childName);
                    currentContentItem.ChildName = childName;
                }
                catch (NotSupportedException)
                {
                    // Ignore. The object just won't have ParentPath or ChildName set.
                }

                // PSDriveInfo

                if (pathInfo.Drive != null)
                {
                    PSDriveInfo drive = pathInfo.Drive;
                    note = new PSNoteProperty("PSDrive", drive);
                    result.Properties.Add(note, true);
                    tracer.WriteLine("Attaching {0} = {1}", "PSDrive", drive);
                    currentContentItem.Drive = drive;
                }

                // ProviderInfo

                ProviderInfo provider = pathInfo.Provider;
                note = new PSNoteProperty("PSProvider", provider);
                result.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSProvider", provider);
                currentContentItem.Provider = provider;
            }

            // Add the ReadCount note
            note = new PSNoteProperty("ReadCount", readCount);
            result.Properties.Add(note, true);

            WriteObject(result);

        } // WriteContentObject

        /// <summary>
        /// A cache of the notes that get added to the content items as they are written
        /// to the pipeline.
        /// </summary>
        private ContentPathsCache currentContentItem;

        /// <summary>
        /// A class that stores a cache of the notes that get attached to content items
        /// as they get written to the pipeline. An instance of this cache class is
        /// only valid for a single path.
        /// </summary>
        internal class ContentPathsCache
        {
            /// <summary>
            /// Constructs a content cache item.
            /// </summary>
            /// 
            /// <param name="pathInfo">
            /// The path information for which the cache will be bound.
            /// </param>
            /// 
            public ContentPathsCache(PathInfo pathInfo)
            {
                this.pathInfo = pathInfo;
            }

            /// <summary>
            /// The path information for the cached item.
            /// </summary>
            /// 
            public PathInfo PathInfo
            {
                get
                {
                    return pathInfo;
                }
            } // PathInfo
            private PathInfo pathInfo;

            /// <summary>
            /// The cached PSPath of the item.
            /// </summary>
            /// 
            public String PSPath
            {
                get
                {
                    return psPath;
                }

                set
                {
                    psPath = value;
                }
            } // PSPath
            private string psPath;

            /// <summary>
            /// The cached parent path of the item.
            /// </summary>
            /// 
            public String ParentPath
            {
                get
                {
                    return parentPath;
                }

                set
                {
                    parentPath = value;
                }
            } // ParentPath
            private string parentPath;

            /// <summary>
            /// The cached drive for the item.
            /// </summary>
            /// 
            public PSDriveInfo Drive
            {
                get
                {
                    return drive;
                }

                set
                {
                    drive = value;
                }
            } // Drive
            private PSDriveInfo drive;

            /// <summary>
            /// The cached provider of the item.
            /// </summary>
            /// 
            public ProviderInfo Provider
            {
                get
                {
                    return provider;
                }

                set
                {
                    provider = value;
                }
            } // Provider
            private ProviderInfo provider;


            /// <summary>
            /// The cached child name of the item.
            /// </summary>
            /// 
            public String ChildName
            {
                get
                {
                    return childName;
                }

                set
                {
                    childName = value;
                }
            } // ChildName
            private string childName;

            /// <summary>
            /// Attaches the cached notes to the specified PSObject.
            /// </summary>
            /// 
            /// <param name="content">
            /// The PSObject to attached the cached notes to.
            /// </param>
            /// 
            /// <returns>
            /// The PSObject that was passed in with the cached notes added.
            /// </returns>
            /// 
            public PSObject AttachNotes(PSObject content)
            {
                // Construct a provider qualified path as the Path note

                PSNoteProperty note = new PSNoteProperty("PSPath", PSPath);
                content.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSPath", PSPath);

                // Now attach the parent path and child name

                note = new PSNoteProperty("PSParentPath", ParentPath);
                content.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSParentPath", ParentPath);

                // Attach the child name

                note = new PSNoteProperty("PSChildName", ChildName);
                content.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSChildName", ChildName);

                // PSDriveInfo

                if (pathInfo.Drive != null)
                {
                    note = new PSNoteProperty("PSDrive", Drive);
                    content.Properties.Add(note, true);
                    tracer.WriteLine("Attaching {0} = {1}", "PSDrive", Drive);
                }

                // ProviderInfo

                note = new PSNoteProperty("PSProvider", Provider);
                content.Properties.Add(note, true);
                tracer.WriteLine("Attaching {0} = {1}", "PSProvider", Provider);

                return content;
            } // AttachNotes
        } // ContentPathsCache


        /// <summary>
        /// A struct to hold the path information and the content readers/writers
        /// for an item.
        /// </summary>
        /// 
        internal struct ContentHolder
        {
            internal ContentHolder(
                PathInfo pathInfo, 
                IContentReader reader, 
                IContentWriter writer)
            {
                if (pathInfo == null)
                {
                    throw PSTraceSource.NewArgumentNullException("pathInfo");
                }

                this._pathInfo = pathInfo;
                this._reader = reader;
                this._writer = writer;
            } // constructor

            internal PathInfo PathInfo
            {
                get { return _pathInfo; }
            }
            private PathInfo _pathInfo;

            internal IContentReader Reader
            {
                get { return _reader; }
            }
            private IContentReader _reader;

            internal IContentWriter Writer
            {
                get { return _writer; }
            }
            private IContentWriter _writer;
        } // struct ContentHolder

        /// <summary>
        /// Closes the content readers and writers in the content holder array
        /// </summary>
        internal void CloseContent(List<ContentHolder> contentHolders, bool disposing)
        {
            if (contentHolders == null)
            {
                throw PSTraceSource.NewArgumentNullException("contentHolders");
            }

            foreach (ContentHolder holder in contentHolders)
            {
                try
                {
                    if (holder.Writer != null)
                    {
                        holder.Writer.Close();
                    }

                }
                catch (Exception e) // Catch-all OK. 3rd party callout
                {
                    CommandsCommon.CheckForSevereException(this, e);
                    // Catch all the exceptions caused by closing the writer
                    // and write out an error.

                    ProviderInvocationException providerException =
                        new ProviderInvocationException(
                            "ProviderContentCloseError",
                            SessionStateStrings.ProviderContentCloseError,
                            holder.PathInfo.Provider,
                            holder.PathInfo.Path,
                            e);


                    // Log a provider health event

                    MshLog.LogProviderHealthEvent(
                        this.Context,
                        holder.PathInfo.Provider.Name,
                        providerException,
                        Severity.Warning);

                    if (!disposing)
                    {
                        WriteError(
                            new ErrorRecord(
                                providerException.ErrorRecord,
                                providerException));
                    }
                }

                try
                {
                    if (holder.Reader != null)
                    {
                        holder.Reader.Close();
                    }
                }
                catch (Exception e) // Catch-all OK. 3rd party callout
                {
                    CommandsCommon.CheckForSevereException(this, e);
                    // Catch all the exceptions caused by closing the writer
                    // and write out an error.

                    ProviderInvocationException providerException =
                        new ProviderInvocationException(
                            "ProviderContentCloseError",
                            SessionStateStrings.ProviderContentCloseError,
                            holder.PathInfo.Provider,
                            holder.PathInfo.Path,
                            e);


                    // Log a provider health event

                    MshLog.LogProviderHealthEvent(
                        this.Context,
                        holder.PathInfo.Provider.Name,
                        providerException,
                        Severity.Warning);

                    if (!disposing)
                    {
                        WriteError(
                            new ErrorRecord(
                                providerException.ErrorRecord,
                                providerException));
                    }
                }

            }
        } // CloseContent

        /// <summary>
        /// Overridden by derived classes to support ShouldProcess with
        /// the appropriate information.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item from which the content writer will be
        /// retrieved.
        /// </param>
        /// 
        /// <returns>
        /// True if the action should continue or false otherwise.
        /// </returns>
        /// 
        internal virtual bool CallShouldProcess(string path)
        {
            return true;
        }

        /// <summary>
        /// Gets the IContentReaders for the current path(s)
        /// </summary>
        /// 
        /// <returns>
        /// An array of IContentReaders for the current path(s)
        /// </returns>
        /// 
        internal List<ContentHolder> GetContentReaders(
            string[] readerPaths,
            CmdletProviderContext currentCommandContext)
        {
            // Resolve all the paths into PathInfo objects

            Collection<PathInfo> pathInfos = ResolvePaths(readerPaths, false, true, currentCommandContext);

            // Create the results array

            List<ContentHolder> results = new List<ContentHolder>();

            foreach (PathInfo pathInfo in pathInfos)
            {
                // For each path, get the content writer

                Collection<IContentReader> readers = null;

                try
                {
                    string pathToProcess = WildcardPattern.Escape(pathInfo.Path);

                    if (currentCommandContext.SuppressWildcardExpansion)
                    {
                        pathToProcess = pathInfo.Path;
                    }

                    readers =
                        InvokeProvider.Content.GetReader(pathToProcess, currentCommandContext);
                }
                catch (PSNotSupportedException notSupported)
                {
                    WriteError(
                        new ErrorRecord(
                            notSupported.ErrorRecord,
                            notSupported));
                    continue;
                }
                catch (DriveNotFoundException driveNotFound)
                {
                    WriteError(
                        new ErrorRecord(
                            driveNotFound.ErrorRecord,
                            driveNotFound));
                    continue;
                }
                catch (ProviderNotFoundException providerNotFound)
                {
                    WriteError(
                        new ErrorRecord(
                            providerNotFound.ErrorRecord,
                            providerNotFound));
                    continue;
                }
                catch (ItemNotFoundException pathNotFound)
                {
                    WriteError(
                        new ErrorRecord(
                            pathNotFound.ErrorRecord,
                            pathNotFound));
                    continue;
                }

                if (readers != null && readers.Count > 0)
                {
                    if (readers.Count == 1 && readers[0] != null)
                    {
                        ContentHolder holder = 
                            new ContentHolder(pathInfo, readers[0], null);

                        results.Add(holder);
                    }
                }
            } // foreach pathInfo in pathInfos

            return results;
        } // GetContentReaders

        /// <summary>
        /// Resolves the specified paths to PathInfo objects
        /// </summary>
        /// 
        /// <param name="pathsToResolve">
        /// The paths to be resolved. Each path may contain glob characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, resolves the path even if it doesn't exist.
        /// </param>
        /// 
        /// <param name="allowEmptyResult">
        /// If true, allows a wildcard that returns no results.
        /// </param>
        /// 
        /// <param name="currentCommandContext">
        /// The context under which the command is running.
        /// </param>
        /// 
        /// <returns>
        /// An array of PathInfo objects that are the resolved paths for the
        /// <paramref name="pathsToResolve"/> parameter.
        /// </returns>
        /// 
        internal Collection<PathInfo> ResolvePaths(
            string[] pathsToResolve, 
            bool allowNonexistingPaths,
            bool allowEmptyResult,
            CmdletProviderContext currentCommandContext)
        {
            Collection<PathInfo> results = new Collection<PathInfo>();

            foreach (string path in pathsToResolve)
            {
                bool pathNotFound = false;
                bool filtersHidPath = false;

                ErrorRecord pathNotFoundErrorRecord = null;

                try
                {

                    // First resolve each of the paths
                    Collection<PathInfo> pathInfos = 
                        SessionState.Path.GetResolvedPSPathFromPSPath(
                            path, 
                            currentCommandContext);

                    if (pathInfos.Count == 0)
                    {
                        pathNotFound = true;

                        // If the item simply did not exist,
                        // we would have got an ItemNotFoundException.
                        // If we get here, it's because the filters
                        // excluded the file.
                        if (!currentCommandContext.SuppressWildcardExpansion)
                        {
                            filtersHidPath = true;
                        }
                    }

                    foreach (PathInfo pathInfo in pathInfos)
                    {
                        results.Add(pathInfo);
                    }
                }
                catch (PSNotSupportedException notSupported)
                {
                    WriteError(
                        new ErrorRecord(
                            notSupported.ErrorRecord,
                            notSupported));
                }
                catch (DriveNotFoundException driveNotFound)
                {
                    WriteError(
                        new ErrorRecord(
                            driveNotFound.ErrorRecord,
                            driveNotFound));
                }
                catch (ProviderNotFoundException providerNotFound)
                {
                    WriteError(
                        new ErrorRecord(
                            providerNotFound.ErrorRecord,
                            providerNotFound));
                }
                catch (ItemNotFoundException pathNotFoundException)
                {
                    pathNotFound = true;
                    pathNotFoundErrorRecord = new ErrorRecord(pathNotFoundException.ErrorRecord, pathNotFoundException);
                }

                if (pathNotFound)
                {
                    if (allowNonexistingPaths &&
                        (! filtersHidPath) &&
                        (currentCommandContext.SuppressWildcardExpansion ||
                        (! WildcardPattern.ContainsWildcardCharacters(path))))
                    {
                        ProviderInfo provider = null;
                        PSDriveInfo drive = null;
                        string unresolvedPath =
                            SessionState.Path.GetUnresolvedProviderPathFromPSPath(
                                path,
                                currentCommandContext,
                                out provider,
                                out drive);

                        PathInfo pathInfo =
                            new PathInfo(
                                drive,
                                provider,
                                unresolvedPath,
                                SessionState);
                        results.Add(pathInfo);
                    }
                    else
                    {
                        if (pathNotFoundErrorRecord == null)
                        {
                            // Detect if the path resolution failed to resolve to a file.
                            String error = StringUtil.Format(NavigationResources.ItemNotFound, Path);
                            Exception e = new Exception(error);

                            pathNotFoundErrorRecord = new ErrorRecord(
                                e,
                                "ItemNotFound",
                                ErrorCategory.ObjectNotFound,
                                Path);
                        }

                        WriteError(pathNotFoundErrorRecord);
                    }
                }
            }

            return results;
        } // ResolvePaths

        #endregion protected members

        #region IDisposable

        internal void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                CloseContent(contentStreams, true);
                contentStreams = new List<ContentHolder>();
            }
        }

        /// <summary>
        /// Dispose method in IDisposeable
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~ContentCommandBase()
        {
            Dispose(false);
        }
        #endregion IDisposable

    } // ContentCommandBase

} // namespace Microsoft.PowerShell.Commands
